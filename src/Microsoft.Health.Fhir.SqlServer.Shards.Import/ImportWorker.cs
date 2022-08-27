// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

////#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
////#pragma warning disable CS8603 // Possible null reference return.
////#pragma warning disable CS8619 // Nullability of reference types in value doesn't match target type.
////using System.Data.SqlClient;
////using Azure.Storage.Blobs;
////using Microsoft.Health.Fhir.Core.Features.Persistence;
////using Microsoft.Health.Fhir.Store.Sharding;
////using Microsoft.Health.Fhir.Store.Utils;

////namespace Microsoft.Health.Fhir.SqlServer.Shards.Import
////{
////    public class ImportWorker
////    {
////        private int _workers = 1;
////        private int _threads = 1;
////        private int _maxRetries = 10;
////        private R4.ResourceParser.ResourceWrapperParser _parser = new();
////        private string _blobStoreConnectionString = string.Empty;
////        private string _blobContainer = string.Empty;

////        public ImportWorker(string connectionString, ISqlServerFhirModel model)
////        {
////            var connStr = SqlService.GetCanonicalConnectionString(connectionString);
////            if (IsSharded(connStr))
////            {
////                SqlService = new SqlService(connStr);
////                _blobStoreConnectionString = GetBlobStoreConnectionString();
////                if (_blobStoreConnectionString == null)
////                {
////                    throw new ArgumentException("_blobStoreConnectionString == null");
////                }

////                _blobContainer = GetBlobContainer();
////                if (_blobContainer == null)
////                {
////                    throw new ArgumentException("_blobContainer == null");
////                }

////                _threads = GetThreads();
////                _workers = GetWorkers();

////                var tasks = new List<Task>();
////                for (var i = 0; i < _workers; i++)
////                {
////                    var worker = i;
////                    tasks.Add(BatchExtensions.StartTask(() =>
////                    {
////                        WaitForSync(worker);
////                        Import(worker);
////                    }));
////                }

////                Task.WaitAll(tasks.ToArray()); // we should never get here
////            }
////        }

////        public SqlService SqlService { get; private set; }

////        private static bool IsSharded(string connectionString)
////        {
////            // handle case when database does not exist yet
////            var builder = new SqlConnectionStringBuilder(connectionString);
////            var db = builder.InitialCatalog;
////            builder.InitialCatalog = "master";
////            using var connDb = new SqlConnection(builder.ToString());
////            connDb.Open();
////            using var cmdDb = new SqlCommand($"IF EXISTS (SELECT * FROM sys.databases WHERE name = @db) SELECT 1 ELSE SELECT 0", connDb);
////            cmdDb.Parameters.AddWithValue("@db", db);
////            var dbExists = (int)cmdDb.ExecuteScalar();
////            if (dbExists == 1)
////            {
////                using var conn = new SqlConnection(connectionString);
////                conn.Open();
////                using var cmd = new SqlCommand("IF object_id('Shards') IS NOT NULL SELECT count(*) FROM dbo.Shards ELSE SELECT 0", conn);
////                var shards = (int)cmd.ExecuteScalar();
////                return shards > 0;
////            }
////            else
////            {
////                return false;
////            }
////        }

////        private (int resourceCount, int insertedCount) ImportBlob(string blobName, TransactionId transactionId)
////        {
////            // read blob and save in batches
////            var lines = 0;
////            BatchExtensions.ExecuteInParallelBatches(GetLinesInBlob(blobName), 1, 100000, (outerThread, batch) =>
////            {
////                BatchExtensions.ExecuteInParallelBatches(batch.Item2, _threads, 100, (innerThread, lineBatch) =>
////                {
////                    foreach (var line in lineBatch.Item2)
////                    {
////                        var resourceWrapper = _parser.CreateResourceWrapper(line.json);
////                        Interlocked.Increment(ref lines);
////                    }
////                });
////            });

////            return (lines, lines);
////        }

////        private IEnumerable<Resource> GetResources(IEnumerable<ResourceWrapper> wrappers)
////        {
////            return wrappers.Select(_ => new Resource());
////        }

////        private IEnumerable<(int index, long offset, string json)> GetLinesInBlob(string blobName)
////        {
////            var index = 0;
////            using var reader = new StreamReader(GetContainer(_blobStoreConnectionString, _blobContainer).GetBlobClient(blobName).Download().Value.Content);
////            while (!reader.EndOfStream)
////            {
////                var json = reader.ReadLine();
////                var offset = 0L;
////                yield return (index, offset, json);
////                index++;
////            }
////        }

////        private void Import(int worker)
////        {
////            while (true)
////            {
////                var retries = 0;
////                var maxRetries = _maxRetries;
////                var version = 0L;
////                var jobId = 0L;
////                var transactionId = new TransactionId(0);
////                var blobName = string.Empty;
////            retry:
////                try
////                {
////                    SqlService.DequeueJob(out var _, out jobId, out version, out blobName);
////                    if (jobId != -1)
////                    {
////                        transactionId = SqlService.BeginTransaction($"queuetype={SqlService.QueueType} jobid={jobId}");
////                        var (resourceCount, totalCount) = ImportBlob(blobName, transactionId);
////                        SqlService.CommitTransaction(transactionId);
////                        SqlService.CompleteJob(jobId, false, version, resourceCount, totalCount);
////                    }
////                    else
////                    {
////                        Thread.Sleep(10000);
////                    }
////                }
////                catch (Exception e)
////                {
////                    SqlService.LogEvent($"Copy", "Error", $"{worker}.{blobName}", text: e.ToString());
////                    retries++;
////                    var isRetryable = e.IsRetryable();
////                    if (isRetryable)
////                    {
////                        maxRetries++;
////                    }

////                    if (retries < maxRetries)
////                    {
////                        Thread.Sleep(isRetryable ? 1000 : 200 * retries);
////                        goto retry;
////                    }

////                    if (transactionId.Id != 0)
////                    {
////                        SqlService.CommitTransaction(transactionId, e.ToString());
////                    }

////                    if (jobId != -1)
////                    {
////                        SqlService.CompleteJob(jobId, true, version);
////                    }
////                }
////            }
////        }

////        private void WaitForSync(int worker)
////        {
////            Console.WriteLine($"Thread={worker} Starting WaitForSync...");
////            using var cmd = new SqlCommand(@"
////WHILE EXISTS (SELECT * FROM dbo.Parameters WHERE Id = 'Copy.Shards.WaitForSync' AND Number = 1)
////BEGIN
////  WAITFOR DELAY '00:00:01'
////END
////              ");
////            cmd.CommandTimeout = 3600;
////            SqlService.ExecuteSqlWithRetries(null, cmd, c => c.ExecuteNonQuery(), 60);
////            Console.WriteLine($"Thread={worker} Completed WaitForSync.");
////        }

////        private static BlobContainerClient GetContainer(string connectionString, string containerName)
////        {
////            var blobServiceClient = new BlobServiceClient(connectionString);
////            var blobContainerClient = blobServiceClient.GetBlobContainerClient(containerName);

////            if (!blobContainerClient.Exists())
////            {
////                throw new ArgumentException($"{containerName} container does not exist.");
////            }

////            return blobContainerClient;
////        }

////        private string GetBlobStoreConnectionString()
////        {
////            using var conn = SqlService.GetConnection(null);
////            using var cmd = new SqlCommand("SELECT Char FROM dbo.Parameters WHERE Id = 'BlobStoreConnectionString'", conn);
////            var str = cmd.ExecuteScalar();
////            return str == DBNull.Value ? null : (string)str;
////        }

////        private string GetBlobContainer()
////        {
////            using var conn = SqlService.GetConnection(null);
////            using var cmd = new SqlCommand("SELECT Char FROM dbo.Parameters WHERE Id = 'BlobContainer'", conn);
////            var str = cmd.ExecuteScalar();
////            return str == DBNull.Value ? null : (string)str;
////        }

////        private int GetThreads()
////        {
////            using var conn = SqlService.GetConnection(null);
////            using var cmd = new SqlCommand("SELECT convert(int,Number) FROM dbo.Parameters WHERE Id = 'ImportWorkerThreads'", conn);
////            var threads = cmd.ExecuteScalar();
////            return threads == DBNull.Value ? 1 : (int)threads;
////        }

////        private int GetWorkers()
////        {
////            using var conn = SqlService.GetConnection(null);
////            using var cmd = new SqlCommand("SELECT convert(int,Number) FROM dbo.Parameters WHERE Id = 'ImportWorkers'", conn);
////            var threads = cmd.ExecuteScalar();
////            return threads == DBNull.Value ? 1 : (int)threads;
////        }
////    }
////}
////#pragma warning restore CS8603 // Possible null reference return.
////#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
////#pragma warning restore CS8619 // Nullability of reference types in value doesn't match target type.
