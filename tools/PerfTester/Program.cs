﻿// -------------------------------------------------------------------------------------------------
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
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Store.Utils;
using Microsoft.Health.Internal.Fhir.Sql;
using Microsoft.Health.SqlServer;
using Microsoft.SqlServer.Management.Sdk.Sfc;
using static Hl7.Fhir.Model.Bundle;

namespace Microsoft.Health.Internal.Fhir.PerfTester
{
    public static class Program
    {
        private static readonly string _connectionString = ConfigurationManager.ConnectionStrings["Database"].ConnectionString;
        private static readonly string _storageConnectionString = ConfigurationManager.AppSettings["StorageConnectionString"];
        private static readonly string _storageUri = ConfigurationManager.AppSettings["StorageUri"];
        private static readonly string _storageUAMI = ConfigurationManager.AppSettings["StorageUAMI"];
        private static readonly string _storageContainerName = ConfigurationManager.AppSettings["StorageContainerName"];
        private static readonly string _storageBlobName = ConfigurationManager.AppSettings["StorageBlobName"];
        private static readonly int _reportingPeriodSec = int.Parse(ConfigurationManager.AppSettings["ReportingPeriodSec"]);
        private static readonly bool _writeResourceIds = bool.Parse(ConfigurationManager.AppSettings["WriteResourceIds"]);
        private static readonly int _threads = int.Parse(ConfigurationManager.AppSettings["Threads"]);
        private static readonly int _calls = int.Parse(ConfigurationManager.AppSettings["Calls"]);
        private static readonly int _bundleSize = int.Parse(ConfigurationManager.AppSettings["BundleSize"]);
        private static readonly string _callType = ConfigurationManager.AppSettings["CallType"];
        private static readonly bool _performTableViewCompare = bool.Parse(ConfigurationManager.AppSettings["PerformTableViewCompare"]);
        private static readonly string _endpoint = ConfigurationManager.AppSettings["FhirEndpoint"];
        private static readonly HttpClient _httpClient = new HttpClient();
        private static readonly string _ndjsonStorageConnectionString = ConfigurationManager.AppSettings["NDJsonStorageConnectionString"];
        private static readonly string _ndjsonStorageUri = ConfigurationManager.AppSettings["NDJsonStorageUri"];
        private static readonly string _ndjsonStorageUAMI = ConfigurationManager.AppSettings["NDJsonStorageUAMI"];
        private static readonly string _ndjsonStorageContainerName = ConfigurationManager.AppSettings["NDJsonStorageContainerName"];
        private static readonly int _takeBlobs = int.Parse(ConfigurationManager.AppSettings["TakeBlobs"]);
        private static readonly int _skipBlobs = int.Parse(ConfigurationManager.AppSettings["SkipBlobs"]);
        private static readonly string _nameFilter = ConfigurationManager.AppSettings["NameFilter"];
        private static readonly bool _writesEnabled = bool.Parse(ConfigurationManager.AppSettings["WritesEnabled"]);
        private static readonly int _repeat = int.Parse(ConfigurationManager.AppSettings["Repeat"]);
        private static readonly int _diagSleepSec = int.Parse(ConfigurationManager.AppSettings["DiagSleepSec"]);

        private static SqlRetryService _sqlRetryService;
        private static SqlStoreClient _store;

        public static void Main()
        {
            Console.WriteLine("!!!See App.config for the details!!!");
            ISqlConnectionBuilder iSqlConnectionBuilder = new Sql.SqlConnectionBuilder(_connectionString);
            _sqlRetryService = SqlRetryService.GetInstance(iSqlConnectionBuilder);
            _store = new SqlStoreClient(_sqlRetryService, NullLogger<SqlStoreClient>.Instance, null);

            _httpClient.Timeout = TimeSpan.FromMinutes(10);

            DumpResourceIds();

            if (_callType == "Diag")
            {
                Diag();
                return;
            }

            if (_callType == "GetDate" || _callType == "LogEvent")
            {
                Console.WriteLine($"Start at {DateTime.UtcNow.ToString("s")}");
                ExecuteParallelCalls(_callType);
                return;
            }

            if (_callType == "SingleId" || _callType == "HttpUpdate" || _callType == "HttpCreate" || _callType == "BundleUpdate")
            {
                Console.WriteLine($"Start at {DateTime.UtcNow.ToString("s")} surrogate Id = {DateTimeOffset.UtcNow.ToSurrogateId()}");
                ExecuteParallelHttpPuts();
                return;
            }

            if (_callType == "BundleCreate")
            {
                Console.WriteLine($"Start at {DateTime.UtcNow.ToString("s")} surrogate Id = {DateTimeOffset.UtcNow.ToSurrogateId()}");
                ExecuteParallelBundleCreates();
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

        private static void Diag()
        {
            Console.WriteLine($"Diag: start at {DateTime.UtcNow.ToString("s")}");
            var container = GetContainer(_ndjsonStorageConnectionString, _ndjsonStorageUri, _ndjsonStorageUAMI, _ndjsonStorageContainerName);
            var swTotal = Stopwatch.StartNew();
            var patients = GetResourceIds("Patient").Select(_ => _.ResourceId).OrderBy(_ => RandomNumberGenerator.GetInt32((int)1e9)).ToList();
            Console.WriteLine($"Diag: read patient ids = {patients.Count} in {(int)swTotal.Elapsed.TotalSeconds} sec.");
            swTotal = Stopwatch.StartNew();
            var observations = GetResourceIds("Observation").Select(_ => _.ResourceId).OrderBy(_ => RandomNumberGenerator.GetInt32((int)1e9)).Take(100000).ToList();
            Console.WriteLine($"Diag: read observation ids = {observations.Count} in {(int)swTotal.Elapsed.TotalSeconds} sec.");
            var loop = 0;
            var observationIndex = 0;
            var patientIndex = 0;
            var swReport = Stopwatch.StartNew();
            swTotal = Stopwatch.StartNew();
            var observationEnumerator = GetLinesInBlobs(container, "Observation").GetEnumerator();
            while (true)
            {
                var id = observations[observationIndex++];
                var sw = Stopwatch.StartNew();
                var status = GetResource("Observation", id);
                _store.TryLogEvent("Diag.Get.Observation", "Warn", $"{(int)sw.Elapsed.TotalMilliseconds} msec status={status} id={id}", null, CancellationToken.None).Wait();

                id = observations[observationIndex++];
                var json = observationEnumerator.MoveNext() ? observationEnumerator.Current : throw new ArgumentException("obervation list is too small");
                ParseJson(ref json, id); // replace id in json
                sw = Stopwatch.StartNew();
                status = PutResource(json, "Observation", id);
                _store.TryLogEvent("Diag.Update.Observation", "Warn", $"{(int)sw.Elapsed.TotalMilliseconds} msec status={status} id={id}", null, CancellationToken.None).Wait();

                id = Guid.NewGuid().ToString();
                json = observationEnumerator.MoveNext() ? observationEnumerator.Current : throw new ArgumentException("obervation list is too small");
                ParseJson(ref json, id); // replace id in json
                sw = Stopwatch.StartNew();
                status = PutResource(json, "Observation", id);
                _store.TryLogEvent("Diag.Create.Observation", "Warn", $"{(int)sw.Elapsed.TotalMilliseconds} msec status={status} id={id}", null, CancellationToken.None).Wait();

                id = observations[observationIndex++];
                DeleteResource("Observation", id, true);

                id = observations[observationIndex++];
                DeleteResource("Observation", id, false);

                id = observations[observationIndex++];
                UpdateSearchParam(id);

                id = patients[patientIndex++];
                GetObservationsForPatient(id);

                id = patients[patientIndex++];
                GetPatientRevIncludeObservations(id);

                id = patients[patientIndex++];
                GetDDMSIncludeIterate(id);

                if (swReport.Elapsed.TotalSeconds > _reportingPeriodSec)
                {
                    lock (swReport)
                    {
                        if (swReport.Elapsed.TotalSeconds > _reportingPeriodSec)
                        {
                            Console.WriteLine($"Diag: loops={loop} elapsed={(int)swTotal.Elapsed.TotalSeconds} sec");
                            swReport.Restart();
                        }
                    }
                }

                Thread.Sleep(_diagSleepSec * 1000);

                loop++;
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

        private static void ExecuteParallelCalls(string callType)
        {
            var callList = new List<int>();
            for (var i = 0; i < _calls; i++)
            {
                callList.Add(i);
            }

            var sw = Stopwatch.StartNew();
            var swReport = Stopwatch.StartNew();
            var calls = 0L;
            long sumLatency = 0;
            Console.WriteLine($"type={callType} threads={_threads} starting...");
            BatchExtensions.ExecuteInParallelBatches(callList, _threads, 1, (thread, item) =>
            {
                Interlocked.Increment(ref calls);
                var swLatency = Stopwatch.StartNew();
                if (callType == "GetDate")
                {
                    GetDate();
                }
                else
                {
                    LogEvent();
                }

                var mcsec = (long)Math.Round(swLatency.Elapsed.TotalMilliseconds * 1000, 0);
                Interlocked.Add(ref sumLatency, mcsec);
                _store.TryLogEvent($"{callType}.threads={_threads}", "Warn", $"mcsec={mcsec}", null, CancellationToken.None).Wait();

                if (swReport.Elapsed.TotalSeconds > _reportingPeriodSec)
                {
                    lock (swReport)
                    {
                        if (swReport.Elapsed.TotalSeconds > _reportingPeriodSec)
                        {
                            Console.WriteLine($"type={callType} threads={_threads} calls={calls} latency={sumLatency / 1000.0 / calls} ms speed={(int)(calls / sw.Elapsed.TotalSeconds)} calls/sec elapsed={(int)sw.Elapsed.TotalSeconds} sec");
                            swReport.Restart();
                        }
                    }
                }
            });

            Console.WriteLine($"type={callType} threads={_threads} calls={calls} latency={sumLatency / 1000.0 / calls} ms speed={(int)(calls / sw.Elapsed.TotalSeconds)} calls/sec elapsed={(int)sw.Elapsed.TotalSeconds} sec");
        }

        private static void LogEvent()
        {
            _store.TryLogEvent("LogEvent", "Warn", $"threads ={_threads}", null, CancellationToken.None).Wait();
        }

        private static void GetDate()
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand("SELECT getUTCdate()", conn);
            cmd.ExecuteNonQuery();
        }

        private static void ExecuteParallelBundleCreates()
        {
            var sourceContainer = GetContainer(_ndjsonStorageConnectionString, _ndjsonStorageUri, _ndjsonStorageUAMI, _ndjsonStorageContainerName);
            var sw = Stopwatch.StartNew();
            var swReport = Stopwatch.StartNew();
            var calls = 0L;
            var errors = 0L;
            var resources = 0;
            long sumLatency = 0;
            BatchExtensions.ExecuteInParallelBatches(GetLinesInBlobs(sourceContainer, _nameFilter), _threads, _bundleSize, (thread, lineItem) =>
            {
                if (Interlocked.Read(ref calls) >= _calls)
                {
                    return;
                }

                var swLatency = Stopwatch.StartNew();

                var entries = new List<string>();
                foreach (var jsonInt in lineItem.Item2)
                {
                    var json = jsonInt;
                    var (resourceType, resourceId) = ParseJson(ref json, Guid.NewGuid().ToString());
                    var entry = GetEntry(json, resourceType, resourceId);
                    entries.Add(entry);
                }

                var status = PostBundleCreate(entries);
                Interlocked.Increment(ref calls);
                Interlocked.Add(ref resources, _bundleSize);
                var mcsec = (long)Math.Round(swLatency.Elapsed.TotalMilliseconds * 1000, 0);
                Interlocked.Add(ref sumLatency, mcsec);
                _store.TryLogEvent($"threads={_threads}.{_callType}:{status}", "Warn", $"mcsec={mcsec}", null, CancellationToken.None).Wait();
                if (swReport.Elapsed.TotalSeconds > _reportingPeriodSec)
                {
                    lock (swReport)
                    {
                        if (swReport.Elapsed.TotalSeconds > _reportingPeriodSec)
                        {
                            Console.WriteLine($"Type={_callType} writes={_writesEnabled} threads={_threads} calls={calls} errors={errors} resources={resources} latency={sumLatency / 1000.0 / calls} ms speed={(int)(resources / sw.Elapsed.TotalSeconds)} RPS elapsed={(int)sw.Elapsed.TotalSeconds} sec");
                            swReport.Restart();
                        }
                    }
                }
            });

            Console.WriteLine($"Type={_callType} writes={_writesEnabled} threads={_threads} calls={calls} errors={errors} resources={resources} latency={sumLatency / 1000.0 / calls} ms speed={(int)(resources / sw.Elapsed.TotalSeconds)} RPS elapsed={(int)sw.Elapsed.TotalSeconds} sec");
        }

        private static void ExecuteParallelHttpPuts()
        {
            var resourceIds = _callType == "HttpUpdate" || _callType == "BundleUpdate" ? GetRandomIds() : new List<(short ResourceTypeId, string ResourceId)>();
            var sourceContainer = GetContainer(_ndjsonStorageConnectionString, _ndjsonStorageUri, _ndjsonStorageUAMI, _ndjsonStorageContainerName);
            var tableOrView = GetResourceObjectType();
            for (var repeat = 0; repeat < _repeat; repeat++)
            {
                var sw = Stopwatch.StartNew();
                var swReport = Stopwatch.StartNew();
                var calls = 0L;
                var errors = 0L;
                var resources = 0;
                long sumLatency = 0;
                var singleId = Guid.NewGuid().ToString();
                BatchExtensions.ExecuteInParallelBatches(GetLinesInBlobs(sourceContainer, _nameFilter), _threads, 1, (thread, lineItem) =>
                {
                    if (Interlocked.Read(ref calls) >= _calls)
                    {
                        return;
                    }

                    var callId = (int)Interlocked.Increment(ref calls) - 1;
                    if ((_callType == "HttpUpdate" || _callType == "BundleUpdate") && callId >= resourceIds.Count)
                    {
                        return;
                    }

                    var resourceIdInput = _callType == "SingleId"
                                        ? singleId
                                        : _callType == "HttpUpdate" || _callType == "BundleUpdate"
                                              ? resourceIds[callId].ResourceId
                                              : Guid.NewGuid().ToString();

                    var swLatency = Stopwatch.StartNew();
                    var json = lineItem.Item2.First();
                    var (resourceType, resourceId) = ParseJson(ref json, resourceIdInput);
                    var status = _callType == "BundleUpdate" ? PostBundle(json, resourceType, resourceId) : PutResource(json, resourceType, resourceId);
                    Interlocked.Increment(ref resources);
                    var mcsec = (long)Math.Round(swLatency.Elapsed.TotalMilliseconds * 1000, 0);
                    Interlocked.Add(ref sumLatency, mcsec);
                    _store.TryLogEvent($"{tableOrView}.threads={_threads}.Put:{status}:{resourceType}/{resourceId}", "Warn", $"mcsec={mcsec}", null, CancellationToken.None).Wait();
                    if (_callType != "BundleUpdate" && status != "OK" && status != "Created")
                    {
                        Interlocked.Increment(ref errors);
                    }

                    if (swReport.Elapsed.TotalSeconds > _reportingPeriodSec)
                    {
                        lock (swReport)
                        {
                            if (swReport.Elapsed.TotalSeconds > _reportingPeriodSec)
                            {
                                Console.WriteLine($"{tableOrView} type={_callType} writes={_writesEnabled} threads={_threads} calls={calls} errors={errors} resources={resources} latency={sumLatency / 1000.0 / calls} ms speed={(int)(calls / sw.Elapsed.TotalSeconds)} calls/sec elapsed={(int)sw.Elapsed.TotalSeconds} sec");
                                swReport.Restart();
                            }
                        }
                    }
                });

                Console.WriteLine($"{tableOrView} type={_callType} writes={_writesEnabled} threads={_threads} calls={calls} errors={errors} resources={resources} latency={sumLatency / 1000.0 / calls} ms speed={(int)(calls / sw.Elapsed.TotalSeconds)} calls/sec elapsed={(int)sw.Elapsed.TotalSeconds} sec");
            }
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

        private static int GetResorceIdsPerCall()
        {
            var resourceIdsPerCall = 1;
            if (_callType.StartsWith("SearchByIds")) // set list of ids
            {
                var split = _callType.Split(':');
                resourceIdsPerCall = int.Parse(split[1]);
            }

            return resourceIdsPerCall;
        }

        private static void ExecuteParallelCalls(ReadOnlyList<(short ResourceTypeId, string ResourceId)> resourceIds)
        {
            var tableOrView = GetResourceObjectType();
            var sw = Stopwatch.StartNew();
            var swReport = Stopwatch.StartNew();
            var calls = 0;
            var errors = 0;
            long sumLatency = 0;
            var resourceIdsPerCall = GetResorceIdsPerCall();
            for (var repeat = 0; repeat < _repeat; repeat++)
            {
                BatchExtensions.ExecuteInParallelBatches(resourceIds, _threads, resourceIdsPerCall, (thread, resourceIds) =>
                {
                    Interlocked.Increment(ref calls);
                    var swLatency = Stopwatch.StartNew();
                    if (_callType == "GetAsync")
                    {
                        var typeId = resourceIds.Item2.First().ResourceTypeId;
                        var id = resourceIds.Item2.First().ResourceId;
                        var first = _store.GetAsync(new[] { new ResourceDateKey(typeId, id, 0, null) }, (s) => "xyz", (i) => typeId.ToString(), true, CancellationToken.None).Result.FirstOrDefault();
                        if (first == null)
                        {
                            Interlocked.Increment(ref errors);
                        }

                        if (first.ResourceId != id)
                        {
                            throw new ArgumentException("Incorrect resource returned");
                        }
                    }
                    else if (_callType.StartsWith("SearchByIds"))
                    {
                        var status = GetResources(_nameFilter, resourceIds.Item2.Select(_ => _.ResourceId));
                        if (status != "OK")
                        {
                            Interlocked.Increment(ref errors);
                        }
                    }
                    else if (_callType == "HttpGet")
                    {
                        var id = resourceIds.Item2.First().ResourceId;
                        var status = GetResource(_nameFilter, id); // apply true translation to type name
                        if (status != "OK")
                        {
                            Interlocked.Increment(ref errors);
                        }
                    }
                    else if (_callType == "HardDeleteNoInvisible")
                    {
                        var typeId = resourceIds.Item2.First().ResourceTypeId;
                        var id = resourceIds.Item2.First().ResourceId;
                        _store.HardDeleteAsync(typeId, id, false, false, CancellationToken.None).Wait();
                    }
                    else if (_callType == "HardDeleteWithInvisible")
                    {
                        var typeId = resourceIds.Item2.First().ResourceTypeId;
                        var id = resourceIds.Item2.First().ResourceId;
                        _store.HardDeleteAsync(typeId, id, false, true, CancellationToken.None).Wait();
                    }
                    else
                    {
                        throw new NotImplementedException();
                    }

                    var mcsec = (long)Math.Round(swLatency.Elapsed.TotalMilliseconds * 1000, 0);
                    Interlocked.Add(ref sumLatency, mcsec);
                    _store.TryLogEvent($"Threads={_threads}.{_callType}.{_nameFilter}", "Warn", $"mcsec={mcsec}", null, CancellationToken.None).Wait();

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
            }

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
            var ids = _calls * GetResorceIdsPerCall();
            Parallel.For(0, (ids / 10) + 10, new ParallelOptions() { MaxDegreeOfParallelism = 64 }, _ =>
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

            var output = results.OrderBy(_ => RandomNumberGenerator.GetInt32(int.MaxValue)).Take(ids).ToList();
            Console.WriteLine($"Selected random ids={output.Count} elapsed={sw.Elapsed.TotalSeconds} secs");
            return output;
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
            foreach (var resourceId in GetResourceIds(_nameFilter))
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
        private static IEnumerable<(short ResourceTypeId, string ResourceId)> GetResourceIds(string nameFilter)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand("SELECT ResourceTypeId FROM dbo.ResourceType WHERE Name = @Name", conn);
            cmd.Parameters.AddWithValue("@Name", nameFilter);
            var ret = cmd.ExecuteScalar();
            if (ret == DBNull.Value)
            {
                using var cmd2 = new SqlCommand("SELECT ResourceTypeId, ResourceId FROM dbo.Resource WHERE IsHistory = 0 AND IsDeleted = 0", conn); // no need to sort to simulate random access
                cmd2.CommandTimeout = 0;
                using var reader = cmd2.ExecuteReader();
                while (reader.Read())
                {
                    yield return (reader.GetInt16(0), reader.GetString(1));
                }
            }
            else
            {
                var resourceTypeId = (short)ret;
                using var cmd2 = new SqlCommand("SELECT ResourceTypeId, ResourceId FROM dbo.Resource WHERE IsHistory = 0 AND IsDeleted = 0 AND ResourceTypeId = @ResourceTypeId OPTION (LOOP JOIN)", conn); // no need to sort to simulate random access
                cmd2.Parameters.AddWithValue("@ResourceTypeId", resourceTypeId);
                cmd2.CommandTimeout = 0;
                using var reader = cmd2.ExecuteReader();
                while (reader.Read())
                {
                    yield return (reader.GetInt16(0), reader.GetString(1));
                }
            }
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
            return GetContainer(_storageConnectionString, _storageUri, _storageUAMI, _storageContainerName);
        }

        private static BlobContainerClient GetContainer(string storageConnectionString, string storageUri, string storageUAMI, string storageContainerName)
        {
            try
            {
                var blobServiceClient = string.IsNullOrEmpty(storageUri) ? new BlobServiceClient(storageConnectionString) : new BlobServiceClient(new Uri(storageUri), string.IsNullOrEmpty(storageUAMI) ? new InteractiveBrowserCredential() : new ManagedIdentityCredential(storageUAMI));
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

        private static IEnumerable<string> GetLinesInBlobs(BlobContainerClient container, string nameFilter)
        {
            var linesRead = 0;
            var blobs = container.GetBlobs().Where(_ => _.Name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase)).Skip(_skipBlobs).Take(_takeBlobs);
            foreach (var blob in blobs)
            {
                if (linesRead >= _calls)
                {
                    break;
                }

                Console.WriteLine($"blob={blob.Name} reading started...");
                using var reader = new StreamReader(container.GetBlobClient(blob.Name).Download().Value.Content);
                while (!reader.EndOfStream)
                {
                    if (linesRead >= _calls)
                    {
                        break;
                    }

                    yield return reader.ReadLine();
                    linesRead++;
                }

                Console.WriteLine($"blob={blob.Name} reading completed. LinesRead={linesRead}");
            }

            Console.WriteLine($"Reading completed. LinesRead={linesRead}");
        }

        private static string PutResource(string jsonString, string resourceType, string resourceId)
        {
            using var content = new StringContent(jsonString, Encoding.UTF8, "application/json");
            var maxRetries = 3;
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
                        GetDate();
                        status = "Skip";
                        break;
                    }

                    var response = _httpClient.PutAsync(uri, content).Result;
                    status = response.StatusCode.ToString();
                    switch (response.StatusCode)
                    {
                        case HttpStatusCode.OK:
                        case HttpStatusCode.Created:
                        case HttpStatusCode.Conflict:
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

        private static string GetBundle(IEnumerable<string> entries)
        {
            var builder = new StringBuilder();
            builder.Append(@"{""resourceType"":""Bundle"",""type"":""transaction"",""entry"":[");
            var first = true;
            foreach (var entry in entries)
            {
                if (!first)
                {
                    builder.Append(',');
                }

                builder.Append(entry);
                first = false;
            }

            builder.Append(@"]}");
            return builder.ToString();
        }

        private static string GetBundle(string entry)
        {
            var builder = new StringBuilder();
            builder.Append(@"{""resourceType"":""Bundle"",""type"":""batch"",""entry"":[");
            builder.Append(entry);
            builder.Append(@"]}");
            return builder.ToString();
        }

        private static string GetEntry(string jsonString, string resourceType, string resourceId)
        {
            var builder = new StringBuilder();
            builder.Append('{')
                   .Append(@"""fullUrl"":""").Append(resourceType).Append('/').Append(resourceId).Append('"')
                   .Append(',').Append(@"""resource"":").Append(jsonString)
                   .Append(',').Append(@"""request"":{""method"":""PUT"",""url"":""").Append(resourceType).Append('/').Append(resourceId).Append(@"""}")
                   .Append('}');
            return builder.ToString();
        }

        private static string PostBundleCreate(IList<string> entries)
        {
            var bundle = GetBundle(entries);
            var maxRetries = 3;
            var retries = 0;
            var networkError = false;
            var bad = false;
            var status = string.Empty;
            do
            {
                var uri = new Uri(_endpoint);
                bad = false;
                try
                {
                    if (!_writesEnabled)
                    {
                        GetDate();
                        status = "Skip";
                        break;
                    }

                    using var content = new StringContent(bundle, Encoding.UTF8, "application/json");
                    using var request = new HttpRequestMessage(HttpMethod.Post, uri);
                    request.Headers.Add("x-bundle-processing-logic", "parallel");
                    request.Content = content;
                    var response = _httpClient.Send(request);
                    if (response.StatusCode == HttpStatusCode.BadRequest)
                    {
                        Console.WriteLine(response.Content.ReadAsStringAsync().Result);
                    }

                    status = response.StatusCode.ToString();
                    switch (response.StatusCode)
                    {
                        case HttpStatusCode.OK:
                            break;
                        default:
                            bad = true;
                            if (response.StatusCode != HttpStatusCode.BadGateway || retries > 0) // too many bad gateway messages in the log
                            {
                                Console.WriteLine($"{DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff")}: Retries={retries} HttpStatusCode={status}");
                            }

                            if (response.StatusCode == HttpStatusCode.TooManyRequests // retry overload errors forever
                                || response.StatusCode == HttpStatusCode.InternalServerError) // 429 on auth cause internal server error
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
                        Console.WriteLine($"Retries={retries} Endpoint={_endpoint} Error={(networkError ? "network" : e.Message)}");
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
                Console.WriteLine($"Failed write. Retries={retries} Endpoint={_endpoint}");
            }

            return status;
        }

        private static string PostBundle(string jsonString, string resourceType, string resourceId)
        {
            var entry = GetEntry(jsonString, resourceType, resourceId);
            var bundle = GetBundle(entry);
            using var content = new StringContent(bundle, Encoding.UTF8, "application/json");
            var maxRetries = 3;
            var retries = 0;
            var networkError = false;
            var bad = false;
            var status = string.Empty;
            do
            {
                var uri = new Uri(_endpoint);
                bad = false;
                try
                {
                    if (!_writesEnabled)
                    {
                        GetDate();
                        status = "Skip";
                        break;
                    }

                    var response = _httpClient.PostAsync(uri, content).Result;
                    status = response.StatusCode.ToString();
                    switch (response.StatusCode)
                    {
                        case HttpStatusCode.OK:
                        case HttpStatusCode.Created:
                        case HttpStatusCode.Conflict:
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

        private static string GetResources(string resourceType, IEnumerable<string> resourceIds)
        {
            var maxRetries = 3;
            var retries = 0;
            var networkError = false;
            var bad = false;
            var status = string.Empty;
            do
            {
                var uri = new Uri(_endpoint + "/" + resourceType + "?_id=" + string.Join(",", resourceIds)) + "&_count=1000";
                bad = false;
                try
                {
                    var response = _httpClient.GetAsync(uri).Result;
                    status = response.StatusCode.ToString();
                    switch (response.StatusCode)
                    {
                        case HttpStatusCode.OK:
                        case HttpStatusCode.InternalServerError:
                            break;
                        default:
                            bad = true;
                            if (response.StatusCode != HttpStatusCode.BadGateway || retries > 0) // too many bad gateway messages in the log
                            {
                                Console.WriteLine($"Retries={retries} Endpoint={_endpoint} HttpStatusCode={status} ResourceType={resourceType}");
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
                        Console.WriteLine($"Retries={retries} Endpoint={_endpoint} ResourceType={resourceType} Error={(networkError ? "network" : e.Message)}");
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
                Console.WriteLine($"Failed readind. Retries={retries} Endpoint={_endpoint}");
            }

            return status;
        }

        private static string GetResource(string resourceType, string resourceId)
        {
            var maxRetries = 3;
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
                    var response = _httpClient.GetAsync(uri).Result;
                    status = response.StatusCode.ToString();
                    switch (response.StatusCode)
                    {
                        case HttpStatusCode.OK:
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
                Console.WriteLine($"Failed reading ResourceType={resourceType} ResourceId={resourceId}. Retries={retries} Endpoint={_endpoint}");
            }

            return status;
        }

        private static void GetObservationsForPatient(string patientId)
        {
            var sw = Stopwatch.StartNew();
            var status = string.Empty;
            var uri = new Uri(_endpoint + "/Observation?patient=" + patientId);
            var count = 0;
            try
            {
                var response = _httpClient.GetAsync(uri).Result;
                status = response.StatusCode.ToString();
                var content = response.Content.ReadAsStringAsync().Result;
                var split = content.Split("fullUrl", StringSplitOptions.None);
                count = split.Length - 1;
            }
            catch (Exception e)
            {
                Console.WriteLine($"uri={uri} error={e.Message}");
                _store.TryLogEvent("Diag.GetObservationsForPatient", "Error", $"id={patientId} error={e.Message}", null, CancellationToken.None).Wait();
            }

            _store.TryLogEvent("Diag.GetObservationsForPatient", "Warn", $"{(int)sw.Elapsed.TotalMilliseconds} msec status={status} count={count} id={patientId}", null, CancellationToken.None).Wait();
        }

        private static void GetDDMSIncludeIterate(string patientId)
        {
            var sw = Stopwatch.StartNew();
            var status = string.Empty;
            var uri = new Uri(_endpoint + $"/DiagnosticReport?status=final&subject=Patient/{patientId}&_include=DiagnosticReport:encounter&_include=DiagnosticReport:result&_include:iterate=DiagnosticReport:results-interpreter&_include:iterate=DiagnosticReport:based-on&_include:iterate=Encounter:location&_include:iterate=DiagnosticReport:performer&_include:iterate=ServiceRequest:requester&_include:iterate=ServiceRequest:encounter&_include:iterate=Location:organization");
            var count = 0;
            try
            {
                var response = _httpClient.GetAsync(uri).Result;
                status = response.StatusCode.ToString();
                var content = response.Content.ReadAsStringAsync().Result;
                var split = content.Split("fullUrl", StringSplitOptions.None);
                count = split.Length - 1;
            }
            catch (Exception e)
            {
                Console.WriteLine($"uri={uri} error={e.Message}");
                _store.TryLogEvent("Diag.GetDDMSIncludeIterate", "Error", $"id={patientId} error={e.Message}", null, CancellationToken.None).Wait();
            }

            _store.TryLogEvent("Diag.GetDDMSIncludeIterate", "Warn", $"{(int)sw.Elapsed.TotalMilliseconds} msec status={status} count={count} id={patientId}", null, CancellationToken.None).Wait();
        }

        private static void GetPatientRevIncludeObservations(string patientId)
        {
            var sw = Stopwatch.StartNew();
            var status = string.Empty;
            var uri = new Uri(_endpoint + "/Patient?_revinclude=Observation:patient&_id=" + patientId);
            var count = 0;
            try
            {
                var response = _httpClient.GetAsync(uri).Result;
                status = response.StatusCode.ToString();
                var content = response.Content.ReadAsStringAsync().Result;
                var split = content.Split("fullUrl", StringSplitOptions.None);
                count = split.Length - 1;
            }
            catch (Exception e)
            {
                Console.WriteLine($"uri={uri} error={e.Message}");
                _store.TryLogEvent("Diag.GetPatientRevIncludeObservations", "Error", $"id={patientId} error={e.Message}", null, CancellationToken.None).Wait();
            }

            _store.TryLogEvent("Diag.GetPatientRevIncludeObservations", "Warn", $"{(int)sw.Elapsed.TotalMilliseconds} msec status={status} count={count} id={patientId}", null, CancellationToken.None).Wait();
        }

        private static void UpdateSearchParam(string resourceId)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                using var conn = new SqlConnection(_connectionString);
                conn.Open();
                using var cmd = new SqlCommand(
                    @"
    DECLARE @Resources dbo.ResourceList
    INSERT INTO @Resources 
         (   ResourceTypeId, ResourceId, RawResource, ResourceSurrogateId, Version, HasVersionToCompare, IsDeleted, IsHistory, KeepHistory, IsRawResourceMetaSet, SearchParamHash) 
      SELECT ResourceTypeId, ResourceId,         0x0, ResourceSurrogateId, Version,                   1,         0,         0,           1,                    1,          'Test'
        FROM Resource
        WHERE ResourceTypeId = 96 AND ResourceId = @ResourceId
    EXECUTE UpdateResourceSearchParams @Resources = @Resources
                    ",
                    conn);
                cmd.Parameters.AddWithValue("@ResourceId", resourceId);
                cmd.ExecuteNonQuery();
            }
            catch (Exception e)
            {
                Console.WriteLine($"Diag.UpdateSearchParams.Observation: Id={resourceId} error={e.Message}");
                _store.TryLogEvent($"Diag.UpdateSearchParams.Observation", "Error", $"id={resourceId} error={e.Message}", null, CancellationToken.None).Wait();
            }

            _store.TryLogEvent($"Diag.UpdateSearchParams.Observation", "Warn", $"{(int)sw.Elapsed.TotalMilliseconds} msec id={resourceId}", null, CancellationToken.None).Wait();

            // check
            try
            {
                using var conn = new SqlConnection(_connectionString);
                conn.Open();
                using var cmd = new SqlCommand("SELECT SearchParamHash FROM Resource WHERE ResourceTypeId = 96 AND IsHistory = 0 AND ResourceId = @ResourceId", conn);
                cmd.Parameters.AddWithValue("@ResourceId", resourceId);
                var hash = (string)cmd.ExecuteScalar();
                if (hash != "Test")
                {
                    throw new ArgumentException("Incorrect hash");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Diag.Check.UpdateSearchParams.Observation: Id={resourceId} error={e.Message}");
                _store.TryLogEvent($"Diag.Check.UpdateSearchParams.Observation", "Error", $"id={resourceId} error={e.Message}", null, CancellationToken.None).Wait();
            }
        }

        private static void DeleteResource(string resourceType, string resourceId, bool isHard)
        {
            var sw = Stopwatch.StartNew();
            var status = string.Empty;
            var uri = new Uri(_endpoint + "/" + resourceType + "/" + resourceId + (isHard ? "?hardDelete=true" : string.Empty));
            try
            {
                var response = _httpClient.DeleteAsync(uri).Result;
                status = response.StatusCode.ToString();
            }
            catch (Exception e)
            {
                Console.WriteLine($"uri={uri} error={e.Message}");
                _store.TryLogEvent($"Diag.{(isHard ? "Hard" : "Soft")}Delete.Observation", "Error", $"id={resourceId} error={e.Message}", null, CancellationToken.None).Wait();
            }

            _store.TryLogEvent($"Diag.{(isHard ? "Hard" : "Soft")}Delete.Observation", "Warn", $"{(int)sw.Elapsed.TotalMilliseconds} msec status={status} id={resourceId}", null, CancellationToken.None).Wait();

            // check
            sw = Stopwatch.StartNew();
            uri = new Uri(_endpoint + "/" + resourceType + "/" + resourceId + "/_history");
            var count = 0;
            try
            {
                var response = _httpClient.GetAsync(uri).Result;
                status = response.StatusCode.ToString();
                var content = response.Content.ReadAsStringAsync().Result;
                var split = content.Split("fullUrl", StringSplitOptions.None);
                count = split.Length - 1;
            }
            catch (Exception e)
            {
                Console.WriteLine($"uri={uri} error={e.Message}");
                _store.TryLogEvent($"Diag.Check{(isHard ? "Hard" : "Soft")}Delete.Observation", "Error", $"id={resourceId} error={e.Message}", null, CancellationToken.None).Wait();
            }

            _store.TryLogEvent($"Diag.Check{(isHard ? "Hard" : "Soft")}Delete.Observation", "Warn", $"{(int)sw.Elapsed.TotalMilliseconds} msec count={count} status={status} id={resourceId}", null, CancellationToken.None).Wait();
        }

        private static bool IsNetworkError(Exception e)
        {
            return e.Message.Contains("connection attempt failed", StringComparison.OrdinalIgnoreCase)
                   || e.Message.Contains("connected host has failed to respond", StringComparison.OrdinalIgnoreCase)
                   || e.Message.Contains("operation on a socket could not be performed", StringComparison.OrdinalIgnoreCase);
        }

        private static (string resourceType, string resourceId) ParseJson(ref string jsonString, string inputResourceId = null)
        {
            var idStart = jsonString.IndexOf("\"id\":\"", StringComparison.OrdinalIgnoreCase) + 6;
            var idShort = jsonString.Substring(idStart, 50);
            var idEnd = idShort.IndexOf("\"", StringComparison.OrdinalIgnoreCase);
            var resourceId = idShort.Substring(0, idEnd);
            if (string.IsNullOrEmpty(resourceId))
            {
                throw new ArgumentException("Cannot parse resource id with string parser");
            }

            if (inputResourceId != null)
            {
                jsonString = jsonString.Replace($"\"id\":\"{resourceId}\"", $"\"id\":\"{inputResourceId}\"", StringComparison.Ordinal);
                resourceId = inputResourceId;
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
