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
using System.Runtime.CompilerServices;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
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

        private static SqlRetryService _sqlRetryService;
        private static SqlStoreClient<SqlServerFhirDataStore> _store;

        public static void Main()
        {
            DumpResourceIds();

            var resourceIds = GetRandomIds();

            ISqlConnectionBuilder iSqlConnectionBuilder = new Sql.SqlConnectionBuilder(_connectionString);
            _sqlRetryService = SqlRetryService.GetInstance(iSqlConnectionBuilder);
            _store = new SqlStoreClient<SqlServerFhirDataStore>(_sqlRetryService, NullLogger<SqlServerFhirDataStore>.Instance);

            var sw = Stopwatch.StartNew();
            var swReport = Stopwatch.StartNew();
            var calls = 0;
            long sumLatency = 0;
            BatchExtensions.ExecuteInParallelBatches(resourceIds, _threads, 1, (thread, resourceId) =>
            {
                Interlocked.Increment(ref calls);
                var swLatency = Stopwatch.StartNew();
                var keys = _store.GetResourceVersionsAsync(new[] { new ResourceDateKey(resourceId.Item2.First().ResourceTypeId, resourceId.Item2.First().ResourceId, 0, null) }, CancellationToken.None).Result;
                Interlocked.Add(ref sumLatency, (long)Math.Round(swLatency.Elapsed.TotalMilliseconds, 0));

                if (swReport.Elapsed.TotalSeconds > _reportingPeriodSec)
                {
                    lock (swReport)
                    {
                        if (swReport.Elapsed.TotalSeconds > _reportingPeriodSec)
                        {
                            Console.WriteLine($"calls={calls} latency={sumLatency / calls} ms speed={(int)(calls / sw.Elapsed.TotalSeconds)} calls/sec");
                            swReport.Restart();
                        }
                    }
                }
            });

            foreach (var resourceId in resourceIds)
            {
                var keys = _store.GetResourceVersionsAsync(new[] { new ResourceDateKey(resourceId.ResourceTypeId, resourceId.ResourceId, 0, null) }, CancellationToken.None).Result;
            }
        }

        private static ReadOnlyList<(short ResourceTypeId, string ResourceId)> GetRandomIds()
        {
            var results = new List<(short ResourceTypeId, string ResourceId)>();

            var container = GetContainer();
            using var stream = container.GetBlockBlobClient(_storageBlobName).OpenRead();
            for (var ids = 0; ids < 100; ids++)
            {
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

        // canot use sqlRetryService as I need IEnumerable
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

        private static BlobContainerClient GetContainer()
        {
            try
            {
                var blobServiceClient = new BlobServiceClient(_storageConnectionString);
                var blobContainerClient = blobServiceClient.GetBlobContainerClient(_storageContainerName);

                if (!blobContainerClient.Exists())
                {
                    var container = blobServiceClient.CreateBlobContainer(_storageContainerName);
                    Console.WriteLine($"Created container {container.Value.Name}");
                }

                return blobContainerClient;
            }
            catch
            {
                Console.WriteLine($"Unable to parse stroage reference or connect to storage account {_storageConnectionString}.");
                throw;
            }
        }
    }
}
