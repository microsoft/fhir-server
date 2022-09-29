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

namespace Microsoft.Health.Fhir.Store.WatchDogs
{
    public class CopyWorker
    {
        private int _workers = 1;
        private bool _writesEnabled = false;
        private int _maxRetries = 10;
        private string _sourceConnectionString = string.Empty;
        private bool _sourceIsSharded = false;

        public CopyWorker(string connectionString)
        {
            Target = new SqlService(connectionString);
            _sourceConnectionString = GetSourceConnectionString();
            if (_sourceConnectionString == null)
            {
                throw new ArgumentException("_sourceConnectionString == null");
            }

            _sourceIsSharded = Workers.IsSharded(_sourceConnectionString);

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
                    Target.DequeueJob(out jobId, out version, out var definition);
                    if (jobId != -1)
                    {
                        var resourceTypeId = (short)0;
                        var resourceCount = 0;
                        var totalCount = 0;
                        if (_sourceIsSharded)
                        {
                            var split = definition.Split(";");
                            resourceTypeId = short.Parse(split[0]);
                            transactionId = new TransactionId(long.Parse(split[1]));
                            (resourceCount, totalCount) = Copy(worker, resourceTypeId, jobId, transactionId);
                        }
                        else
                        {
                            var split = definition.Split(";");
                            resourceTypeId = short.Parse(split[0]);
                            var minId = long.Parse(split[1]);
                            var maxId = long.Parse(split[2]);
                            var suffix = split.Length > 3 ? split[3] : null;
                            transactionId = Target.BeginTransaction($"queuetype={SqlService.QueueType} jobid={jobId}");
                            (resourceCount, totalCount) = Copy(worker, resourceTypeId, jobId, minId, maxId, transactionId, suffix);
                            Target.CommitTransaction(transactionId);
                        }

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

        private (int resourceCnt, int totalCnt) Copy(int thread, short resourceTypeId, long jobId, TransactionId transactionId)
        {
            var sw = Stopwatch.StartNew();
            var resources = GetData(_ => new Resource(_, true), resourceTypeId, transactionId);
            var referenceSearchParams = GetData(_ => new ReferenceSearchParam(_, true), resourceTypeId, transactionId);
            var tokenSearchParams = GetData(_ => new TokenSearchParam(_, true), resourceTypeId, transactionId);
            var compartmentAssignments = GetData(_ => new CompartmentAssignment(_, true), resourceTypeId, transactionId);
            var tokenTexts = GetData(_ => new TokenText(_, true), resourceTypeId, transactionId);
            var dateTimeSearchParams = GetData(_ => new DateTimeSearchParam(_, true), resourceTypeId, transactionId);
            var tokenQuantityCompositeSearchParams = GetData(_ => new TokenQuantityCompositeSearchParam(_, true), resourceTypeId, transactionId);
            var quantitySearchParams = GetData(_ => new QuantitySearchParam(_, true), resourceTypeId, transactionId);
            var stringSearchParams = GetData(_ => new StringSearchParam(_, true), resourceTypeId, transactionId);
            var tokenTokenCompositeSearchParams = GetData(_ => new TokenTokenCompositeSearchParam(_, true), resourceTypeId, transactionId);
            var tokenStringCompositeSearchParams = GetData(_ => new TokenStringCompositeSearchParam(_, true), resourceTypeId, transactionId);
            var rows = 0;
            if (_writesEnabled)
            {
                rows = Target.MergeResources(transactionId, resources, referenceSearchParams, tokenSearchParams, compartmentAssignments, tokenTexts, dateTimeSearchParams, tokenQuantityCompositeSearchParams, quantitySearchParams, stringSearchParams, tokenTokenCompositeSearchParams, tokenStringCompositeSearchParams);
            }

            Console.WriteLine($"Copy.{thread}.{jobId}.{resourceTypeId}.{transactionId}: completed at {DateTime.Now:s}, elapsed={sw.Elapsed.TotalSeconds:N0} sec.");
            return (resources.Count, rows);
        }

#pragma warning disable SA1107 // Code should not contain multiple statements on one line
#pragma warning disable SA1127 // Generic type constraints should be on their own line
        private (int resourceCnt, int totalCnt) Copy(int thread, short resourceTypeId, long jobId, long minId, long maxId, TransactionId transactionId, string suffix)
        {
            var sw = Stopwatch.StartNew();
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
            var compartmentAssignments = GetData(_ => new CompartmentAssignment(_, false, suffix), resourceTypeId, minId, maxId, surrIdMap, transactionId);
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
#pragma warning restore SA1107 // Code should not contain multiple statements on one line
#pragma warning restore SA1127 // Generic type constraints should be on their own line

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "No user input")]
        private IList<T> GetData<T>(Func<SqlDataReader, T> toT, short resourceTypeId, TransactionId transactionId)
        {
            var results = new List<T>();
            ShardedSource.ParallelForEachShard(
                (shardId) =>
                {
                    using var cmd = new SqlCommand($"SELECT * FROM dbo.{typeof(T).Name} WHERE ResourceTypeId = @ResourceTypeId AND TransactionId = @TransactionId") { CommandTimeout = 600 };
                    cmd.Parameters.AddWithValue("@ResourceTypeId", resourceTypeId);
                    cmd.Parameters.AddWithValue("@TransactionId", transactionId.Id);
                    ShardedSource.ExecuteSqlWithRetries(
                        shardId,
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
            if (results.Count == 0)
            {
                results = null;
            }

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

        private string GetSourceConnectionString()
        {
            using var conn = Target.GetConnection();
            using var cmd = new SqlCommand("SELECT Char FROM dbo.Parameters WHERE Id = 'Copy.SourceConnectionString'", conn);
            var str = cmd.ExecuteScalar();
            return str == null ? null : (string)str;
        }

        private int GetWorkers()
        {
            using var conn = Target.GetConnection();
            using var cmd = new SqlCommand("SELECT convert(int,Number) FROM dbo.Parameters WHERE Id = 'Copy.Workers'", conn);
            var threads = cmd.ExecuteScalar();
            return threads == null ? 1 : (int)threads;
        }

        private bool GetWritesEnabled()
        {
            using var conn = Target.GetConnection();
            using var cmd = new SqlCommand("SELECT convert(bit,Number) FROM dbo.Parameters WHERE Id = 'Copy.WritesEnabled'", conn);
            var flag = cmd.ExecuteScalar();
            return flag != null && (bool)flag;
        }
    }
}
