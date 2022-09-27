// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Store.Sharding;
using Microsoft.Health.Fhir.Store.Utils;

namespace Microsoft.Health.Fhir.Store.Copy
{
    public class CopyWorker
    {
        private int _workers = 1;
        private bool _writesEnabled = false;
        private int _maxRetries = 10;
        private string _targetConnectionString = string.Empty;
        private string _sourceConnectionString = string.Empty;
        private bool _sourceIsSharded = false;

        public CopyWorker(string connectionString)
        {
            var connStr = SqlUtils.SqlService.GetCanonicalConnectionString(connectionString);
            var configService = new SqlUtils.SqlService(connStr);
            _targetConnectionString = GetTargetConnectionString(configService);
            if (IsSharded(_targetConnectionString))
            {
                Target = new SqlService(_targetConnectionString);
                _sourceConnectionString = GetSourceConnectionString();
                if (_sourceConnectionString == null)
                {
                    throw new ArgumentException("_sourceConnectionString == null");
                }

                _sourceIsSharded = IsSharded(_sourceConnectionString);

                if (_sourceIsSharded)
                {
                    ShardedSource = new SqlService(_sourceConnectionString);
                }

                _workers = GetWorkers();
                _writesEnabled = GetWritesEnabled();

                var tasks = new List<Task>();
                var workingTasks = 0L;
                for (var i = 0; i < _workers; i++)
                {
                    var worker = i;
                    tasks.Add(BatchExtensions.StartTask(() =>
                    {
                        Interlocked.Increment(ref workingTasks);
                        Copy(worker);
                        Interlocked.Decrement(ref workingTasks);
                    }));
                }

                Thread.Sleep(2000); //// Try to wait till increments happen
                Target.LogEvent($"Copy", "Warn", string.Empty, text: $"workingTasks={Interlocked.Read(ref workingTasks)}");

                //// TODO: Move to watchdog
                ////tasks.Add(BatchExtensions.StartTask(() =>
                ////{
                ////    AdvanceVisibility(ref workingTasks);
                ////}));

                ////Task.WaitAll(tasks.ToArray()); // we should never get here
            }
        }

        public SqlService Target { get; private set; }

        public SqlService ShardedSource { get; private set; }

        private void AdvanceVisibility(ref int workingTasks)
        {
            var affected = 1;
            while (workingTasks > 0 || affected > 0)
            {
                affected = Target.AdvanceTransactionVisibility();
                if (affected == 0)
                {
                    Thread.Sleep(1000);
                }
            }
        }

        private void Copy(int worker)
        {
            while (true)
            {
                var retries = 0;
                var maxRetries = _maxRetries;
                var version = 0L;
                var jobId = 0L;
                var transactionId = new TransactionId(0);
            retry:
                try
                {
                    short? resourceTypeId;
                    var minId = 0L;
                    var maxId = 0L;
                    var suffix = string.Empty;
                    var sourceTransactionId = new TransactionId(0);
                    if (_sourceIsSharded)
                    {
                        Target.DequeueJob(out jobId, out version, out resourceTypeId, out sourceTransactionId, out suffix);
                    }
                    else
                    {
                        Target.DequeueJob(out jobId, out version, out resourceTypeId, out minId, out maxId, out suffix);
                    }

                    if (jobId != -1)
                    {
                        transactionId = Target.BeginTransaction($"queuetype={SqlService.QueueType} jobid={jobId}");
                        var (resourceCount, totalCount) = _sourceIsSharded
                                                        ? Copy(worker, resourceTypeId.Value, jobId, sourceTransactionId, transactionId, suffix)
                                                        : Copy(worker, resourceTypeId.Value, jobId, minId, maxId, transactionId, suffix);
                        Target.CommitTransaction(transactionId);
                        Target.CompleteJob(jobId, false, version, resourceCount, totalCount, transactionId.Id);
                    }
                    else
                    {
                        Thread.Sleep(10000);
                    }
                }
                catch (Exception e)
                {
                    Target.LogEvent($"Copy", "Error", $"{worker}.{jobId}", text: e.ToString());
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
                        Target.CommitTransaction(transactionId, e.ToString());
                    }

                    if (jobId != -1)
                    {
                        Target.CompleteJob(jobId, true, version);
                    }
                }
            }
        }

#pragma warning disable SA1107 // Code should not contain multiple statements on one line
#pragma warning disable SA1127 // Generic type constraints should be on their own line
        private (int resourceCnt, int totalCnt) Copy(int thread, short resourceTypeId, long jobId, TransactionId sourceTransactionId, TransactionId transactionId, string suffix)
        {
            var sw = Stopwatch.StartNew();
            var st = DateTime.UtcNow;
            var shardletSequence = new Dictionary<ShardletId, short>();
            var surrIdMap = new Dictionary<long, (ShardletId ShardletId, short Sequence)>(); // map from surr id to shardlet resource index
            var resources = GetData(_ => new Resource(_, false, suffix), resourceTypeId, sourceTransactionId);

            foreach (var resource in resources)
            {
                var shardletId = ShardletId.GetHashedShardletId(resource.ResourceId);
                if (!shardletSequence.TryGetValue(shardletId, out var sequence))
                {
                    shardletSequence.Add(shardletId, 0);
                }
                else
                {
                    sequence++;
                    shardletSequence[shardletId] = sequence;
                }

                if (!surrIdMap.ContainsKey(resource.ResourceSurrogateId))
                {
                    surrIdMap.Add(resource.ResourceSurrogateId, (shardletId, sequence));
                }
            }

            resources = resources.Select(_ => { _.TransactionId = transactionId; (var shardletId, var sequence) = surrIdMap[_.ResourceSurrogateId]; _.ShardletId = shardletId; _.Sequence = sequence; return _; }).ToList();

            var referenceSearchParams = GetData(_ => new ReferenceSearchParam(_, false, suffix), resourceTypeId, sourceTransactionId, transactionId);
            var tokenSearchParams = GetData(_ => new TokenSearchParam(_, false), resourceTypeId, sourceTransactionId, transactionId);
            var compartmentAssignments = GetData(_ => new CompartmentAssignment(_, false), resourceTypeId, sourceTransactionId, transactionId);
            var tokenTexts = GetData(_ => new TokenText(_, false), resourceTypeId, sourceTransactionId, transactionId);
            var dateTimeSearchParams = GetData(_ => new DateTimeSearchParam(_, false), resourceTypeId, sourceTransactionId, transactionId);
            var tokenQuantityCompositeSearchParams = GetData(_ => new TokenQuantityCompositeSearchParam(_, false), resourceTypeId, sourceTransactionId, transactionId);
            var quantitySearchParams = GetData(_ => new QuantitySearchParam(_, false), resourceTypeId, sourceTransactionId, transactionId);
            var stringSearchParams = GetData(_ => new StringSearchParam(_, false), resourceTypeId, sourceTransactionId, transactionId);
            var tokenTokenCompositeSearchParams = GetData(_ => new TokenTokenCompositeSearchParam(_, false), resourceTypeId, sourceTransactionId, transactionId);
            var tokenStringCompositeSearchParams = GetData(_ => new TokenStringCompositeSearchParam(_, false), resourceTypeId, sourceTransactionId, transactionId);

            var rows = 0;
            if (_writesEnabled)
            {
                rows = Target.MergeResources(transactionId, resources, referenceSearchParams, tokenSearchParams, compartmentAssignments, tokenTexts, dateTimeSearchParams, tokenQuantityCompositeSearchParams, quantitySearchParams, stringSearchParams, tokenTokenCompositeSearchParams, tokenStringCompositeSearchParams);
            }

            Console.WriteLine($"Copy.{thread}.{jobId}.{resourceTypeId}.{sourceTransactionId}: completed at {DateTime.Now:s}, elapsed={sw.Elapsed.TotalSeconds:N0} sec.");

            return (resources.Count, rows);
        }

        private (int resourceCnt, int totalCnt) Copy(int thread, short resourceTypeId, long jobId, long minId, long maxId, TransactionId transactionId, string suffix)
        {
            var sw = Stopwatch.StartNew();
            var st = DateTime.UtcNow;
            var shardletSequence = new Dictionary<ShardletId, short>();
            var surrIdMap = new Dictionary<long, (ShardletId ShardletId, short Sequence)>(); // map from surr id to shardlet resource index
            var resources = GetData(_ => new Resource(_, false, suffix), resourceTypeId, minId, maxId);

            foreach (var resource in resources)
            {
                var shardletId = ShardletId.GetHashedShardletId(resource.ResourceId);
                if (!shardletSequence.TryGetValue(shardletId, out var sequence))
                {
                    shardletSequence.Add(shardletId, 0);
                }
                else
                {
                    sequence++;
                    shardletSequence[shardletId] = sequence;
                }

                if (!surrIdMap.ContainsKey(resource.ResourceSurrogateId))
                {
                    surrIdMap.Add(resource.ResourceSurrogateId, (shardletId, sequence));
                }
            }

            resources = resources.Select(_ => { _.TransactionId = transactionId; (var shardletId, var sequence) = surrIdMap[_.ResourceSurrogateId]; _.ShardletId = shardletId; _.Sequence = sequence; return _; }).ToList();

            var referenceSearchParams = GetData(_ => new ReferenceSearchParam(_, false, suffix), resourceTypeId, minId, maxId, surrIdMap, transactionId);
            var tokenSearchParams = GetData(_ => new TokenSearchParam(_, false), resourceTypeId, minId, maxId, surrIdMap, transactionId);
            var compartmentAssignments = GetData(_ => new CompartmentAssignment(_, false), resourceTypeId, minId, maxId, surrIdMap, transactionId);
            var tokenTexts = GetData(_ => new TokenText(_, false), resourceTypeId, minId, maxId, surrIdMap, transactionId);
            var dateTimeSearchParams = GetData(_ => new DateTimeSearchParam(_, false), resourceTypeId, minId, maxId, surrIdMap, transactionId);
            var tokenQuantityCompositeSearchParams = GetData(_ => new TokenQuantityCompositeSearchParam(_, false), resourceTypeId, minId, maxId, surrIdMap, transactionId);
            var quantitySearchParams = GetData(_ => new QuantitySearchParam(_, false), resourceTypeId, minId, maxId, surrIdMap, transactionId);
            var stringSearchParams = GetData(_ => new StringSearchParam(_, false), resourceTypeId, minId, maxId, surrIdMap, transactionId);
            var tokenTokenCompositeSearchParams = GetData(_ => new TokenTokenCompositeSearchParam(_, false), resourceTypeId, minId, maxId, surrIdMap, transactionId);
            var tokenStringCompositeSearchParams = GetData(_ => new TokenStringCompositeSearchParam(_, false), resourceTypeId, minId, maxId, surrIdMap, transactionId);

            var rows = 0;
            if (_writesEnabled)
            {
                rows = Target.MergeResources(transactionId, resources, referenceSearchParams, tokenSearchParams, compartmentAssignments, tokenTexts, dateTimeSearchParams, tokenQuantityCompositeSearchParams, quantitySearchParams, stringSearchParams, tokenTokenCompositeSearchParams, tokenStringCompositeSearchParams);
            }

            Console.WriteLine($"Copy.{thread}.{jobId}.{resourceTypeId}.{minId}: completed at {DateTime.Now:s}, elapsed={sw.Elapsed.TotalSeconds:N0} sec.");

            return (resources.Count, rows);
        }

        private IList<T> GetData<T>(Func<SqlDataReader, T> toT, short resourceTypeId, long minId, long maxId, Dictionary<long, (ShardletId ShardletId, short Sequence)> surrIdMap, TransactionId transactionId) where T : PrimaryKey
        {
            var results = GetData(toT, resourceTypeId, minId, maxId);
            if (results.Count == 0)
            {
                results = null;
            }
            else
            {
                results = results.Where(_ => surrIdMap.ContainsKey(_.ResourceSurrogateId)).Select(_ => { _.TransactionId = transactionId; (var shardletId, var sequence) = surrIdMap[_.ResourceSurrogateId]; _.ShardletId = shardletId; _.Sequence = sequence; return _; }).ToList();
            }

            return results;
        }

        private IList<T> GetData<T>(Func<SqlDataReader, T> toT, short resourceTypeId, TransactionId sourceTransactionId, TransactionId transactionId) where T : PrimaryKey
        {
            var results = GetData(toT, resourceTypeId, transactionId);
            if (results.Count == 0)
            {
                results = null;
            }
            else
            {
                results = results.Select(_ => { _.TransactionId = transactionId; return _; }).ToList();
            }

            return results;
        }
#pragma warning restore SA1107 // Code should not contain multiple statements on one line
#pragma warning restore SA1127 // Generic type constraints should be on their own line

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "No user input")]
        private IList<T> GetData<T>(Func<SqlDataReader, T> toT, short resourceTypeId, TransactionId transactionId)
        {
            var results = new List<T>();
            using var cmd = new SqlCommand($"SELECT * FROM dbo.{typeof(T).Name} WHERE ResourceTypeId = @ResourceTypeId AND TransactionId = @TransactionId") { CommandTimeout = 600 };
            cmd.Parameters.AddWithValue("@ResourceTypeId", resourceTypeId);
            cmd.Parameters.AddWithValue("@TransactionId", transactionId);
            ShardedSource.ParallelForEachShard(
                (shardId) =>
                {
                    SqlUtils.SqlService.ExecuteSqlWithRetries(
                    _sourceConnectionString,
                    cmd,
                    cmdInt =>
                    {
                        var resultsInt = new List<T>();
                        using var reader = cmdInt.ExecuteReader();
                        while (reader.Read())
                        {
                            results.Add(toT(reader));
                        }

                        reader.NextResult();

                        lock (results)
                        {
                            results.AddRange(resultsInt);
                        }
                    });
                },
                null);
            return results;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "No user input")]
        private IList<T> GetData<T>(Func<SqlDataReader, T> toT, short resourceTypeId, long minId, long maxId)
        {
            List<T> results = null;
            using var cmd = new SqlCommand($"SELECT * FROM dbo.{typeof(T).Name} WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId BETWEEN @MinId AND @MaxId ORDER BY ResourceSurrogateId") { CommandTimeout = 600 };
            cmd.Parameters.AddWithValue("@ResourceTypeId", resourceTypeId);
            cmd.Parameters.AddWithValue("@MinId", minId);
            cmd.Parameters.AddWithValue("@MaxId", maxId);
            SqlUtils.SqlService.ExecuteSqlWithRetries(
                _sourceConnectionString,
                cmd,
                cmdInt =>
                {
                    results = new List<T>();
                    using var reader = cmdInt.ExecuteReader();
                    while (reader.Read())
                    {
                        results.Add(toT(reader));
                    }

                    reader.NextResult();
                });
            return results;
        }

        private static bool IsSharded(string connectionString)
        {
            if (connectionString == null)
            {
                return false;
            }

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

        private static string GetTargetConnectionString(SqlUtils.SqlService configService)
        {
            using var conn = configService.GetConnection();
            using var cmd = new SqlCommand("SELECT Char FROM dbo.Parameters WHERE Id = 'Copy.TargetConnectionString'", conn);
            var str = cmd.ExecuteScalar();
            return str == DBNull.Value ? null : (string)str;
        }

        private string GetSourceConnectionString()
        {
            using var conn = Target.GetConnection();
            using var cmd = new SqlCommand("SELECT Char FROM dbo.Parameters WHERE Id = 'Copy.SourceConnectionString'", conn);
            var str = cmd.ExecuteScalar();
            return str == DBNull.Value ? null : (string)str;
        }

        private int GetWorkers()
        {
            using var conn = Target.GetConnection();
            using var cmd = new SqlCommand("SELECT convert(int,Number) FROM dbo.Parameters WHERE Id = 'Copy.Workers'", conn);
            var threads = cmd.ExecuteScalar();
            return threads == DBNull.Value ? 1 : (int)threads;
        }

        private bool GetWritesEnabled()
        {
            using var conn = Target.GetConnection();
            using var cmd = new SqlCommand("SELECT convert(bit,Number) FROM dbo.Parameters WHERE Id = 'Copy.WritesEnabled'", conn);
            var flag = cmd.ExecuteScalar();
            return flag == DBNull.Value ? false : (bool)flag;
        }
    }
}
