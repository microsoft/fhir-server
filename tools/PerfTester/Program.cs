// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#pragma warning disable CA2100
#pragma warning disable CA1303
using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using DotLiquid.Tags;
using ICSharpCode.SharpZipLib.Core;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Store.Utils;
using Microsoft.Health.SqlServer;
using Microsoft.SqlServer.Management.Sdk.Sfc;

namespace Microsoft.Health.Internal.Fhir.PerfTester
{
    public static class Program
    {
        private static readonly string _connectionString = ConfigurationManager.ConnectionStrings["Database"].ConnectionString;
        private static readonly string _storageConnectionString = ConfigurationManager.AppSettings["StorageConnectionString"];
        private static readonly string _storageContainerName = ConfigurationManager.AppSettings["StorageContainerName"];
        private static readonly string _storageBlobName = ConfigurationManager.AppSettings["StorageBlobName"];
        private static readonly int _reportingPeriodSec = int.Parse(ConfigurationManager.AppSettings["ReportingPeriodSec"]);
        private static readonly bool _writeResourceIds = bool.Parse(ConfigurationManager.AppSettings["WriteResourceIds"]);
        private static readonly int _threads = int.Parse(ConfigurationManager.AppSettings["Threads"]);
        private static readonly int _calls = int.Parse(ConfigurationManager.AppSettings["Calls"]);
        private static readonly string _callType = ConfigurationManager.AppSettings["CallType"];
        private static readonly bool _performTableViewCompare = bool.Parse(ConfigurationManager.AppSettings["PerformTableViewCompare"]);
        private static readonly string _endpoint = ConfigurationManager.AppSettings["FhirEndpoint"];
        private static readonly HttpClient _httpClient = new HttpClient();
        private static readonly string _sourceStorageContainerName = ConfigurationManager.AppSettings["SourceStorageContainerName"];
        private static readonly int _takeBlobs = int.Parse(ConfigurationManager.AppSettings["TakeBlobs"]);
        private static readonly int _skipBlobs = int.Parse(ConfigurationManager.AppSettings["SkipBlobs"]);
        private static readonly string _nameFilter = ConfigurationManager.AppSettings["NameFilter"];
        private static readonly bool _writesEnabled = bool.Parse(ConfigurationManager.AppSettings["WritesEnabled"]);

        private static SqlRetryService _sqlRetryService;
        private static SqlStoreClient<SqlServerFhirDataStore> _store;

        public static void Main()
        {
            Console.WriteLine("!!!See App.config for the details!!!");
            ISqlConnectionBuilder iSqlConnectionBuilder = new Sql.SqlConnectionBuilder(_connectionString);
            _sqlRetryService = SqlRetryService.GetInstance(iSqlConnectionBuilder);
            _store = new SqlStoreClient<SqlServerFhirDataStore>(_sqlRetryService, NullLogger<SqlServerFhirDataStore>.Instance);

            if (_callType == "HttpPut")
            {
                Console.WriteLine($"Start at {DateTime.UtcNow.ToString("s")} surrogate Id = {ResourceSurrogateIdHelper.LastUpdatedToResourceSurrogateId(DateTime.UtcNow)}");
                ExecuteParallelCalls();
                return;
            }

            if (_callType == "GetByTransactionId")
            {
                var tranIds = GetRandomTransactionIds();
                SwitchToResourceTable();
                ExecuteParallelCalls(tranIds);
                if (_performTableViewCompare)
                {
                    SwitchToResourceView();
                    ExecuteParallelCalls(tranIds);
                }

                return;
            }

            DumpResourceIds();

            var resourceIds = GetRandomIds();
            SwitchToResourceTable();
            ExecuteParallelCalls(resourceIds); // compare this
            if (_performTableViewCompare)
            {
                SwitchToResourceView();
                ExecuteParallelCalls(resourceIds);
                resourceIds = GetRandomIds();
                ExecuteParallelCalls(resourceIds); // compare this
                SwitchToResourceTable();
                ExecuteParallelCalls(resourceIds);
            }
        }

        private static ReadOnlyList<long> GetRandomTransactionIds()
        {
            var sw = Stopwatch.StartNew();
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand("SELECT SurrogateIdRangeFirstValue FROM dbo.Transactions", conn);
            using var reader = cmd.ExecuteReader();
            var tranIds = new List<long>();
            while (reader.Read())
            {
                tranIds.Add(reader.GetInt64(0));
            }

            tranIds = tranIds.OrderBy(_ => RandomNumberGenerator.GetInt32((int)1e9)).Take(_calls).ToList();
            Console.WriteLine($"Selected random transaction ids={tranIds.Count} elapsed={sw.Elapsed.TotalSeconds} secs");
            return tranIds;
        }

        private static void ExecuteParallelCalls()
        {
            var sourceContainer = GetContainer(_storageConnectionString, _sourceStorageContainerName);
            var blobs = sourceContainer.GetBlobs().Where(_ => _.Name.Contains(_nameFilter, StringComparison.OrdinalIgnoreCase)).Skip(_skipBlobs).Take(_takeBlobs);
            var tableOrView = GetResourceObjectType();
            var sw = Stopwatch.StartNew();
            var swReport = Stopwatch.StartNew();
            var calls = 0L;
            var resources = 0;
            long sumLatency = 0;
            foreach (var blob in blobs)
            {
                if (Interlocked.Read(ref calls) >= _calls)
                {
                    break;
                }

                Console.WriteLine($"{tableOrView} blob={blob.Name} started...");
                var lines = GetLinesInBlob(sourceContainer, blob.Name);
                BatchExtensions.ExecuteInParallelBatches(lines, _threads, 1, (thread, lineItem) =>
                {
                    if (Interlocked.Read(ref calls) >= _calls)
                    {
                        return;
                    }

                    Interlocked.Increment(ref calls);
                    var swLatency = Stopwatch.StartNew();
                    var json = lineItem.Item2.First();
                    var (resourceType, resourceId) = ParseJson(json);
                    var status = PutResource(json, resourceType, resourceId);
                    Interlocked.Increment(ref resources);
                    var mcsec = (long)Math.Round(swLatency.Elapsed.TotalMilliseconds * 1000, 0);
                    Interlocked.Add(ref sumLatency, mcsec);
                    _store.TryLogEvent($"{tableOrView}.threads={_threads}.Put:{status}:{resourceType}/{resourceId}", "Warn", $"mcsec={mcsec}", null, CancellationToken.None).Wait();

                    if (swReport.Elapsed.TotalSeconds > _reportingPeriodSec)
                    {
                        lock (swReport)
                        {
                            if (swReport.Elapsed.TotalSeconds > _reportingPeriodSec)
                            {
                                Console.WriteLine($"{tableOrView} type=HttpPut writes={_writesEnabled} threads={_threads} calls={calls} resources={resources} latency={sumLatency / 1000.0 / calls} ms speed={(int)(calls / sw.Elapsed.TotalSeconds)} calls/sec elapsed={(int)sw.Elapsed.TotalSeconds} sec");
                                swReport.Restart();
                            }
                        }
                    }
                });
            }

            Console.WriteLine($"{tableOrView} type=HttpPut writes={_writesEnabled} threads={_threads} calls={calls} resources={resources} latency={sumLatency / 1000.0 / calls} ms speed={(int)(calls / sw.Elapsed.TotalSeconds)} calls/sec elapsed={(int)sw.Elapsed.TotalSeconds} sec");
        }

        private static void ExecuteParallelCalls(ReadOnlyList<long> tranIds)
        {
            var tableOrView = GetResourceObjectType();
            var sw = Stopwatch.StartNew();
            var swReport = Stopwatch.StartNew();
            var calls = 0;
            var resources = 0;
            long sumLatency = 0;
            BatchExtensions.ExecuteInParallelBatches(tranIds, _threads, 1, (thread, id) =>
            {
                Interlocked.Increment(ref calls);
                var swLatency = Stopwatch.StartNew();
                var tranId = id.Item2.First();
                var res = _store.GetResourcesByTransactionIdAsync(tranId, (s) => "xyz", (i) => "xyz", CancellationToken.None).Result;
                Interlocked.Add(ref resources, res.Count);
                Interlocked.Add(ref sumLatency, (long)Math.Round(swLatency.Elapsed.TotalMilliseconds * 1000, 0));

                if (swReport.Elapsed.TotalSeconds > _reportingPeriodSec)
                {
                    lock (swReport)
                    {
                        if (swReport.Elapsed.TotalSeconds > _reportingPeriodSec)
                        {
                            Console.WriteLine($"{tableOrView} type=GetResourcesByTransactionIdAsync threads={_threads} calls={calls} resources={resources} latency={sumLatency / 1000.0 / calls} ms speed={(int)(calls / sw.Elapsed.TotalSeconds)} calls/sec elapsed={(int)sw.Elapsed.TotalSeconds} sec");
                            swReport.Restart();
                        }
                    }
                }
            });
            Console.WriteLine($"{tableOrView} type=GetResourcesByTransactionIdAsync threads={_threads} calls={calls} resources={resources} latency={sumLatency / 1000.0 / calls} ms speed={(int)(calls / sw.Elapsed.TotalSeconds)} calls/sec elapsed={(int)sw.Elapsed.TotalSeconds} sec");
        }

        private static void ExecuteParallelCalls(ReadOnlyList<(short ResourceTypeId, string ResourceId)> resourceIds)
        {
            var tableOrView = GetResourceObjectType();
            var sw = Stopwatch.StartNew();
            var swReport = Stopwatch.StartNew();
            var calls = 0;
            var errors = 0;
            long sumLatency = 0;
            BatchExtensions.ExecuteInParallelBatches(resourceIds, _threads, 1, (thread, resourceId) =>
            {
                Interlocked.Increment(ref calls);
                var swLatency = Stopwatch.StartNew();
                if (_callType == "GetAsync")
                {
                    var typeId = resourceId.Item2.First().ResourceTypeId;
                    var id = resourceId.Item2.First().ResourceId;
                    var first = _store.GetAsync(new[] { new ResourceDateKey(typeId, id, 0, null) }, (s) => "xyz", (i) => typeId.ToString(), CancellationToken.None).Result.FirstOrDefault();
                    if (first == null)
                    {
                        Interlocked.Increment(ref errors);
                    }

                    if (first.ResourceId != id)
                    {
                        throw new ArgumentException("Incorrect resource returned");
                    }
                }
                else if (_callType == "HardDeleteNoChangeCapture")
                {
                    var typeId = resourceId.Item2.First().ResourceTypeId;
                    var id = resourceId.Item2.First().ResourceId;
                    _store.HardDeleteAsync(typeId, id, false, false, CancellationToken.None).Wait();
                }
                else if (_callType == "HardDeleteWithChangeCapture")
                {
                    var typeId = resourceId.Item2.First().ResourceTypeId;
                    var id = resourceId.Item2.First().ResourceId;
                    _store.HardDeleteAsync(typeId, id, false, true, CancellationToken.None).Wait();
                }
                else
                {
                    throw new NotImplementedException();
                }

                Interlocked.Add(ref sumLatency, (long)Math.Round(swLatency.Elapsed.TotalMilliseconds * 1000, 0));

                if (swReport.Elapsed.TotalSeconds > _reportingPeriodSec)
                {
                    lock (swReport)
                    {
                        if (swReport.Elapsed.TotalSeconds > _reportingPeriodSec)
                        {
                            Console.WriteLine($"{tableOrView} type={_callType} threads={_threads} calls={calls} errors={errors} latency={sumLatency / 1000.0 / calls} ms speed={(int)(calls / sw.Elapsed.TotalSeconds)} calls/sec elapsed={(int)sw.Elapsed.TotalSeconds} sec");
                            swReport.Restart();
                        }
                    }
                }
            });
            Console.WriteLine($"{tableOrView} type={_callType} threads={_threads} calls={calls} errors={errors} latency={sumLatency / 1000.0 / calls} ms speed={(int)(calls / sw.Elapsed.TotalSeconds)} calls/sec elapsed={(int)sw.Elapsed.TotalSeconds} sec");
        }

        private static void SwitchToResourceTable()
        {
            if (!_performTableViewCompare)
            {
                return;
            }

            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand(
                @"
IF EXISTS (SELECT * FROM sys.objects WHERE name = 'Resource' AND type = 'v')
BEGIN
  BEGIN TRY
    BEGIN TRANSACTION
    
    EXECUTE sp_rename 'Resource', 'Resource_View'

    EXECUTE sp_rename 'Resource_Table', 'Resource'

    COMMIT TRANSACTION
  END TRY
  BEGIN CATCH
    IF @@trancount > 0 ROLLBACK TRANSACTION;
    THROW
  END CATCH
END
                ",
                conn);
            cmd.ExecuteNonQuery();
            Console.WriteLine("Switched to resource table");
        }

        private static void SwitchToResourceView()
        {
            if (!_performTableViewCompare)
            {
                return;
            }

            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand(
                @"
IF EXISTS (SELECT * FROM sys.objects WHERE name = 'Resource' AND type = 'u')
BEGIN
  BEGIN TRY
    BEGIN TRANSACTION
    
    EXECUTE sp_rename 'Resource', 'Resource_Table'

    EXECUTE sp_rename 'Resource_View', 'Resource'

    COMMIT TRANSACTION
  END TRY
  BEGIN CATCH
    IF @@trancount > 0 ROLLBACK TRANSACTION;
    THROW
  END CATCH
END
                ",
                conn);
            cmd.ExecuteNonQuery();
            Console.WriteLine("Switched to resource view");
        }

        private static ReadOnlyList<(short ResourceTypeId, string ResourceId)> GetRandomIds()
        {
            var sw = Stopwatch.StartNew();

            var results = new HashSet<(short ResourceTypeId, string ResourceId)>();

            var container = GetContainer();
            var size = container.GetBlockBlobClient(_storageBlobName).GetProperties().Value.ContentLength;
            size -= 1000 * 10; // will get 10 ids in single seek. never go to the exact end, so there is always room for 10 ids from offset. designed for large data sets. 600M ids = 25GB.
            Parallel.For(0, _calls / 10, new ParallelOptions() { MaxDegreeOfParallelism = 64 }, _ =>
            {
                var resourceIds = GetRandomIdsBySingleOffset(container, size);
                lock (results)
                {
                    foreach (var resourceId in resourceIds)
                    {
                        results.Add(resourceId);
                    }
                }
            });

            Console.WriteLine($"Selected random ids={results.Count} elapsed={sw.Elapsed.TotalSeconds} secs");
            return results.ToList();
        }

        private static ReadOnlyList<(short ResourceTypeId, string ResourceId)> GetRandomIdsBySingleOffset(BlobContainerClient container, long size)
        {
            var bucketSize = 100000L;
            var buckets = (int)(size / bucketSize);
            long offset;
            if (buckets > 1000)
            {
                var firstRand = RandomNumberGenerator.GetInt32(buckets);
                var secondRand = (long)RandomNumberGenerator.GetInt32((int)bucketSize);
                offset = (firstRand * bucketSize) + secondRand;
            }
            else
            {
                offset = RandomNumberGenerator.GetInt32((int)size);
            }

            using var reader = new StreamReader(container.GetBlockBlobClient(_storageBlobName).OpenRead(offset));
            int lines = 0;
            var results = new List<(short ResourceTypeId, string ResourceId)>();
            while (!reader.EndOfStream && lines < 10)
            {
                reader.ReadLine(); // skip first line as it might be imcomplete
                var line = reader.ReadLine();
                var split = line.Split('\t');
                lines++;
                if (split.Length != 2)
                {
                    throw new ArgumentOutOfRangeException("incorrect length");
                }

                results.Add((short.Parse(split[0]), split[1]));
            }

            return results;
        }

        private static void DumpResourceIds()
        {
            if (!_writeResourceIds)
            {
                return;
            }

            var sw = Stopwatch.StartNew();
            var swReport = Stopwatch.StartNew();
            var lines = 0L;
            var container = GetContainer();
            using var stream = container.GetBlockBlobClient(_storageBlobName).OpenWrite(true);
            using var writer = new StreamWriter(stream);
            foreach (var resourceId in GetResourceIds())
            {
                lines++;
                writer.WriteLine($"{resourceId.ResourceTypeId}\t{resourceId.ResourceId}");
                if (swReport.Elapsed.TotalSeconds > _reportingPeriodSec)
                {
                    lock (swReport)
                    {
                        if (swReport.Elapsed.TotalSeconds > _reportingPeriodSec)
                        {
                            Console.WriteLine($"ResourceIds={lines} secs={(int)sw.Elapsed.TotalSeconds} speed={(int)(lines / sw.Elapsed.TotalSeconds)} resourceIds/sec");
                            swReport.Restart();
                        }
                    }
                }
            }

            writer.Flush();

            Console.WriteLine($"ResourceIds={lines} secs={(int)sw.Elapsed.TotalSeconds} speed={(int)(lines / sw.Elapsed.TotalSeconds)} resourceIds/sec");
        }

        // cannot use sqlRetryService as I need IEnumerable
        private static IEnumerable<(short ResourceTypeId, string ResourceId)> GetResourceIds()
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand("SELECT ResourceTypeId, ResourceId FROM dbo.Resource WHERE IsHistory = 0 ORDER BY ResourceTypeId, ResourceId OPTION (MAXDOP 1)", conn);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                yield return (reader.GetInt16(0), reader.GetString(1));
            }
        }

        private static void PingDatabase()
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand("SELECT 1", conn);
            cmd.ExecuteNonQuery();
        }

        private static string GetResourceObjectType()
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand("SELECT type_desc FROM sys.objects WHERE name = 'Resource'", conn);
            return (string)cmd.ExecuteScalar();
        }

        private static BlobContainerClient GetContainer()
        {
            return GetContainer(_storageConnectionString, _storageContainerName);
        }

        private static BlobContainerClient GetContainer(string storageConnectionString, string storageContainerName)
        {
            try
            {
                var blobServiceClient = new BlobServiceClient(storageConnectionString);
                var blobContainerClient = blobServiceClient.GetBlobContainerClient(storageContainerName);

                if (!blobContainerClient.Exists())
                {
                    var container = blobServiceClient.CreateBlobContainer(storageContainerName);
                    Console.WriteLine($"Created container {container.Value.Name}");
                }

                return blobContainerClient;
            }
            catch
            {
                Console.WriteLine($"Unable to parse stroage reference or connect to storage account {storageConnectionString}.");
                throw;
            }
        }

        private static IEnumerable<string> GetLinesInBlob(BlobContainerClient container, string blobName)
        {
            using var reader = new StreamReader(container.GetBlobClient(blobName).Download().Value.Content);
            while (!reader.EndOfStream)
            {
                yield return reader.ReadLine();
            }
        }

        private static string PutResource(string jsonString, string resourceType, string resourceId)
        {
            using var content = new StringContent(jsonString, Encoding.UTF8, "application/json");
            var maxRetries = 10;
            var retries = 0;
            var networkError = false;
            var bad = false;
            var status = string.Empty;
            do
            {
                var uri = new Uri(_endpoint + "/" + resourceType + "/" + resourceId);
                bad = false;
                try
                {
                    if (!_writesEnabled)
                    {
                        PingDatabase();
                        status = "Skip";
                        break;
                    }

                    var response = _httpClient.PutAsync(uri, content).Result;
                    status = response.StatusCode.ToString();
                    switch (response.StatusCode)
                    {
                        case HttpStatusCode.OK:
                        case HttpStatusCode.Created:
                        case HttpStatusCode.InternalServerError:
                            break;
                        default:
                            bad = true;
                            if (response.StatusCode != HttpStatusCode.BadGateway || retries > 0) // too many bad gateway messages in the log
                            {
                                Console.WriteLine($"Retries={retries} Endpoint={_endpoint} HttpStatusCode={status} ResourceType={resourceType} ResourceId={resourceId}");
                            }

                            if (response.StatusCode == HttpStatusCode.TooManyRequests) // retry overload errors forever
                            {
                                maxRetries++;
                            }

                            break;
                    }
                }
                catch (Exception e)
                {
                    networkError = IsNetworkError(e);
                    if (!networkError)
                    {
                        Console.WriteLine($"Retries={retries} Endpoint={_endpoint} ResourceType={resourceType} ResourceId={resourceId} Error={(networkError ? "network" : e.Message)}");
                    }

                    bad = true;
                    if (networkError) // retry network errors forever
                    {
                        maxRetries++;
                    }
                }

                if (bad && retries < maxRetries)
                {
                    retries++;
                    Thread.Sleep(networkError ? 1000 : 200 * retries);
                }
            }
            while (bad && retries < maxRetries);
            if (bad)
            {
                Console.WriteLine($"Failed writing ResourceType={resourceType} ResourceId={resourceId}. Retries={retries} Endpoint={_endpoint}");
            }

            return status;
        }

        private static bool IsNetworkError(Exception e)
        {
            return e.Message.Contains("connection attempt failed", StringComparison.OrdinalIgnoreCase)
                   || e.Message.Contains("connected host has failed to respond", StringComparison.OrdinalIgnoreCase)
                   || e.Message.Contains("operation on a socket could not be performed", StringComparison.OrdinalIgnoreCase);
        }

        private static (string resourceType, string resourceId) ParseJson(string jsonString)
        {
            var idStart = jsonString.IndexOf("\"id\":\"", StringComparison.OrdinalIgnoreCase) + 6;
            var idShort = jsonString.Substring(idStart, 50);
            var idEnd = idShort.IndexOf("\"", StringComparison.OrdinalIgnoreCase);
            var resourceId = idShort.Substring(0, idEnd);
            if (string.IsNullOrEmpty(resourceId))
            {
                throw new ArgumentException("Cannot parse resource id with string parser");
            }

            var rtStart = jsonString.IndexOf("\"resourceType\":\"", StringComparison.OrdinalIgnoreCase) + 16;
            var rtShort = jsonString.Substring(rtStart, 50);
            var rtEnd = rtShort.IndexOf("\"", StringComparison.OrdinalIgnoreCase);
            var resourceType = rtShort.Substring(0, rtEnd);
            if (string.IsNullOrEmpty(resourceType))
            {
                throw new ArgumentException("Cannot parse resource type with string parser");
            }

            return (resourceType, resourceId);
        }
    }
}
