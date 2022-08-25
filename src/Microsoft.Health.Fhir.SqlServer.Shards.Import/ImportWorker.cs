// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
using System.Data.SqlClient;
using Microsoft.Health.Fhir.Store.Sharding;
using Microsoft.Health.Fhir.Store.Utils;

namespace Microsoft.Health.Fhir.SqlServer.Shards.Import
{
    public class ImportWorker
    {
        private int _threads = Environment.ProcessorCount * 2;
        private int _maxRetries = 10;

        public ImportWorker(string connectionString)
        {
            var connStr = SqlService.GetCanonicalConnectionString(connectionString);
            if (IsSharded(connStr))
            {
                SqlService = new SqlService(connStr);
                var tasks = new List<Task>();
                for (var i = 0; i < _threads; i++)
                {
                    var thread = i;
                    tasks.Add(BatchExtensions.StartTask(() =>
                    {
                        WaitForSync(thread);
                        Import(thread);
                    }));
                }
            }
        }

        public SqlService SqlService { get; private set; }

        private static bool IsSharded(string connectionString)
        {
            // handle case when database does not exist yet
            var builder = new SqlConnectionStringBuilder(connectionString);
            var db = builder.InitialCatalog;
            builder.InitialCatalog = "master";
            using var connDb = new SqlConnection(builder.ToString());
            connDb.Open();
            using var cmdDb = new SqlCommand($"IF EXISTS (SELECT * FROM sys.databases WHERE name = @db) SELECT 1 ELSE SELECT 0", connDb);
            cmdDb.Parameters.AddWithValue("@db", db);
            var dbExists = (int)cmdDb.ExecuteScalar();
            if (dbExists == 1)
            {
                using var conn = new SqlConnection(connectionString);
                conn.Open();
                using var cmd = new SqlCommand("IF object_id('Shards') IS NOT NULL SELECT count(*) FROM dbo.Shards ELSE SELECT 0", conn);
                var shards = (int)cmd.ExecuteScalar();
                return shards > 0;
            }
            else
            {
                return false;
            }
        }

        private (int resourceCount, int totalCount) ImportBlob(string blobLocation, TransactionId transactionId)
        {
            // read blob and save in batches
            var str = blobLocation;
            SqlService.LogEvent($"ImportBlob", "Run", str);
            ////var resourceWrapper = _resourceWrapperFactory.Create(str.Tofalse, false);
            ////var resource = parser.Parse<Resource>(str);
            ////var resourceElement = resource.ToResourceElement();
            ////var wrapper = wrapperFactory.Create(resourceElement, false, true);

            return (0, 0);
        }

        private void Import(int thread)
        {
            var retries = 0;
            var maxRetries = _maxRetries;
            var version = 0L;
            var jobId = 0L;
            var transactionId = new TransactionId(0);
            var blobLocation = string.Empty;
        retry:
            try
            {
                SqlService.DequeueJob(out var _, out jobId, out version, out blobLocation);
                if (jobId != -1)
                {
                    transactionId = SqlService.BeginTransaction($"queuetype={SqlService.QueueType} jobid={jobId}");
                    var (resourceCount, totalCount) = ImportBlob(blobLocation, transactionId);
                    SqlService.CommitTransaction(transactionId);
                    SqlService.CompleteJob(jobId, false, version, resourceCount, totalCount);
                }
            }
            catch (Exception e)
            {
                SqlService.LogEvent($"Copy", "Error", $"{thread}.{blobLocation}", text: e.ToString());
                retries++;
                var isRetryable = e.IsRetryable();
                if (isRetryable)
                {
                    maxRetries++;
                }

                if (retries < maxRetries)
                {
                    Thread.Sleep(isRetryable ? 1000 : 200 * retries);
                    goto retry;
                }

                if (transactionId.Id != 0)
                {
                    SqlService.CommitTransaction(transactionId, e.ToString());
                }

                if (jobId != -1)
                {
                    SqlService.CompleteJob(jobId, true, version);
                }

                throw;
            }
        }

        private void WaitForSync(int thread)
        {
            Console.WriteLine($"Thread={thread} Starting WaitForSync...");
            using var cmd = new SqlCommand(@"
WHILE EXISTS (SELECT * FROM dbo.Parameters WHERE Id = 'Copy.Shards.WaitForSync' AND Number = 1)
BEGIN
  WAITFOR DELAY '00:00:01'
END
              ");
            cmd.CommandTimeout = 3600;
            SqlService.ExecuteSqlWithRetries(null, cmd, c => c.ExecuteNonQuery(), 60);
            Console.WriteLine($"Thread={thread} Completed WaitForSync.");
        }
    }
}
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
