// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.SqlServer.Database;
using Microsoft.Health.Fhir.Store.Sharding;
using Microsoft.Health.Fhir.Store.Utils;

namespace Microsoft.Health.Fhir.Store.Copy
{
    public static class Program
    {
        private static readonly string SourceConnectionString = ConfigurationManager.ConnectionStrings["SourceDatabase"].ConnectionString;
        private static readonly string TargetConnectionString = ConfigurationManager.ConnectionStrings["TargetDatabase"].ConnectionString;
        private static readonly string QueueConnectionString = ConfigurationManager.ConnectionStrings["QueueDatabase"].ConnectionString;
        private static readonly string ShardsFilter = ConfigurationManager.AppSettings["ShardsFilter"];
        private static readonly int Threads = int.Parse(ConfigurationManager.AppSettings["Threads"]);
        private static readonly int UnitSize = int.Parse(ConfigurationManager.AppSettings["UnitSize"]);
        private static readonly int MaxRetries = int.Parse(ConfigurationManager.AppSettings["MaxRetries"]);
        private static readonly bool RebuildQueue = bool.Parse(ConfigurationManager.AppSettings["RebuildQueue"]);
        private static readonly bool QueueOnly = bool.Parse(ConfigurationManager.AppSettings["QueueOnly"]);
        private static readonly bool TransactionsOnly = bool.Parse(ConfigurationManager.AppSettings["TransactionsOnly"]);
        private static readonly bool WritesEnabled = bool.Parse(ConfigurationManager.AppSettings["WritesEnabled"]);
        private static readonly bool RunAdvanceVisibilityWatchDog = bool.Parse(ConfigurationManager.AppSettings["RunAdvanceVisibilityWatchDog"]);
        private static bool stop = false;
        [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.NamingRules", "SA1306:Field names should begin with lower-case letter", Justification = "Readability")]
        private static SqlService Target;
        private static readonly SqlServiceSingleDatabase Source = new SqlServiceSingleDatabase(SourceConnectionString);
        [System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.NamingRules", "SA1306:Field names should begin with lower-case letter", Justification = "Readability")]
        private static SqlService Queue;

        public static void Main(string[] args)
        {
            Console.WriteLine($"Source=[{Source.ShowConnectionString()}]");
            Console.WriteLine($"Target=[{TargetConnectionString}]");
            Console.WriteLine($"Queue=[{QueueConnectionString}]");
            var method = args.Length > 0 ? args[0] : "query";
            if (method == "setupdb")
            {
                SetupDb.Publish(TargetConnectionString, "Microsoft.Health.Fhir.SqlServer.Database.Central.dacpac");
                Target = new SqlService(TargetConnectionString);
                foreach (var shard in Target.ShardletMap.Shards)
                {
                    SetupDb.Publish(shard.Value.ConnectionString, "Microsoft.Health.Fhir.SqlServer.Database.Distributed.dacpac");
                }
            }
            else if (method == "init")
            {
                Target = new SqlService(TargetConnectionString);
                foreach (var shard in Target.ShardletMap.Shards)
                {
                    TruncateTables(shard.Key);
                    Console.WriteLine($"Truncated tables in {shard.Value.Server}..{shard.Value.Database}.");
                    DisableIndexes(shard.Key);
                    Console.WriteLine($"Disabled indexes in {shard.Value.Server}..{shard.Value.Database}.");
                }
            }
            else if (method == "rebuildqueue")
            {
                Target = new SqlService(TargetConnectionString);
                Queue = new SqlService(QueueConnectionString);
                PopulateJobQueue(UnitSize);
            }
            else if (method == "merge")
            {
                Target = new SqlService(TargetConnectionString);
                Queue = new SqlService(QueueConnectionString);
                PopulateJobQueue(UnitSize);
                Copy();
            }
            else
            {
                Target = new SqlService(TargetConnectionString);
                Console.WriteLine($"RunQuery: started at {DateTime.UtcNow:s}...");
                RunQuery();
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "No user input")]
        private static void RunQuery()
        {
            var shardIds = ShardsFilter.Split(",").Select(_ => new ShardId(byte.Parse(_))).ToList();
            if (shardIds.Count == 0)
            {
                shardIds = Target.ShardIds.ToList();
            }

            Console.WriteLine($"RunQuery: ShardIds={string.Join(',', shardIds)}.");

            var sw = Stopwatch.StartNew();
            var top = 11;
            var q0 = $@"
DECLARE @p0 smallint = 1016 -- http://hl7.org/fhir/SearchParameter/Patient-name
DECLARE @p1 nvarchar(256) = 'Kesha802' -- 281
DECLARE @p2 smallint = 103 -- Patient
DECLARE @p3 smallint = 217 -- http://hl7.org/fhir/SearchParameter/clinical-patient
DECLARE @p4 smallint = 96 -- Observation
DECLARE @p5 smallint = 202 -- http://hl7.org/fhir/SearchParameter/clinical-code
DECLARE @p6 varchar(128) = '9279-1'
DECLARE @p7 int = {top}
                ";
            var q1 = @"
SELECT ResourceTypeId, ResourceId, TransactionId, ShardletId, Sequence
  FROM dbo.Resource Patient
  WHERE Patient.IsHistory = 0
    AND Patient.ResourceTypeId = @p2 -- Patient
    AND EXISTS 
          (SELECT *
             FROM dbo.StringSearchParam
             WHERE IsHistory = 0
               AND ResourceTypeId = @p2 -- Patient
               AND SearchParamId = @p0 -- type of name
               AND Text LIKE @p1 -- name text
               AND TransactionId = Patient.TransactionId AND ShardletId = Patient.ShardletId AND Sequence = Patient.Sequence
          )
  OPTION (RECOMPILE)
                ";
            var q2 = @"
SELECT ResourceTypeId, ResourceId, TransactionId, ShardletId, Sequence 
  FROM @ResourceKeys Patient
  WHERE EXISTS 
          (SELECT *
             FROM dbo.ReferenceSearchParam Observ
             WHERE IsHistory = 0
               AND ResourceTypeId = @p4 -- Observation
               AND SearchParamId = @p3 -- clinical-patient
               AND ReferenceResourceTypeId = @p2 -- Patient
               AND ReferenceResourceId = Patient.ResourceId
               AND EXISTS
                     (SELECT *
                        FROM dbo.TokenSearchParam
                        WHERE IsHistory = 0
                          AND ResourceTypeId = @p4 -- Observation
                          AND SearchParamId = @p5 -- clinical-code
                          AND Code = @p6 -- code text
                          AND TransactionId = Observ.TransactionId AND ShardletId = Observ.ShardletId AND Sequence = Observ.Sequence
                     )
          )
  OPTION (RECOMPILE)
                ";

            // get resource keys
            var resourceKeys = new List<ResourceKey>();
            SqlService.ParallelForEachShard(
                shardIds,
                (shardId) =>
                {
                    using var cmd = new SqlCommand(@$"{q0}{q1}") { CommandTimeout = 600 };
                    Target.ExecuteSqlWithRetries(
                        shardId,
                        cmd,
                        cmdInt =>
                        {
                            var keys = new List<ResourceKey>();
                            using var reader = cmdInt.ExecuteReader();
                            while (reader.Read())
                            {
                                keys.Add(new ResourceKey(reader));
                            }

                            reader.NextResult();

                            lock (resourceKeys)
                            {
                                resourceKeys.AddRange(keys);
                            }
                        });
                },
                null);

            // Check resource keys
            var checkedResourceKeys = new List<ResourceKey>();
            SqlService.ParallelForEachShard(
                shardIds,
                (shardId) =>
                {
                    using var cmd = new SqlCommand(@$"{q0}{q2}") { CommandTimeout = 600 };
                    var resourceKeysParam = new SqlParameter { ParameterName = "@ResourceKeys" };
                    resourceKeysParam.AddResourceKeyList(resourceKeys);
                    cmd.Parameters.Add(resourceKeysParam);
                    Target.ExecuteSqlWithRetries(
                        shardId,
                        cmd,
                        cmdInt =>
                        {
                            var keys = new List<ResourceKey>();
                            using var reader = cmdInt.ExecuteReader();
                            while (reader.Read())
                            {
                                keys.Add(new ResourceKey(reader));
                            }

                            reader.NextResult();

                            lock (checkedResourceKeys)
                            {
                                checkedResourceKeys.AddRange(keys);
                            }
                        });
                },
                null);

            // return final
            var final = checkedResourceKeys.OrderBy(_ => _.TransactionId.Id).ThenBy(_ => _.ShardletId.Id).ThenBy(_ => _.Sequence).Take(top).ToList();
            Console.WriteLine($"RunQuery: completed in {(int)sw.Elapsed.TotalMilliseconds} milliseconds at {DateTime.UtcNow:s}.");
        }

        private static void TruncateTables(ShardId shardId)
        {
            using var cmd = new SqlCommand(@"
DECLARE @cmd varchar(max) = ''
SELECT @cmd = @cmd + 'TRUNCATE TABLE '+O.name+char(10)
  FROM (SELECT * FROM sys.indexes WHERE index_id IN (0,1)) I
       JOIN sys.objects O ON O.object_id = I.object_id
  WHERE EXISTS (SELECT * FROM sys.partition_schemes PS WHERE PS.data_space_id = I.data_space_id AND name = 'PartitionScheme_ResourceTypeId')
EXECUTE(@cmd)
              ");
            Target.ExecuteSqlWithRetries(shardId, cmd, c => c.ExecuteNonQuery(), 60);
        }

        private static void DisableIndexes(ShardId shardId)
        {
            using var cmd = new SqlCommand(@"
DECLARE @cmd varchar(max) = ''
SELECT @cmd = @cmd + 'ALTER INDEX '+I.name+' ON '+O.name+' DISABLE'+char(10)
  FROM (SELECT * FROM sys.indexes WHERE index_id NOT IN (0,1)) I
       JOIN sys.objects O ON O.object_id = I.object_id
  WHERE EXISTS (SELECT * FROM sys.partition_schemes PS WHERE PS.data_space_id = I.data_space_id AND name = 'PartitionScheme_ResourceTypeId')
EXECUTE(@cmd)
              ");
            Target.ExecuteSqlWithRetries(shardId, cmd, c => c.ExecuteNonQuery(), 60);
        }

        private static void WaitForSync(int thread)
        {
            Console.WriteLine($"Thread={thread} Starting WaitForSync...");
            using var cmd = new SqlCommand(@"
WHILE EXISTS (SELECT * FROM dbo.Parameters WHERE Id = 'Copy.Shards.WaitForSync' AND Number = 1)
BEGIN
  WAITFOR DELAY '00:00:01'
END
              ");
            cmd.CommandTimeout = 3600;
            Target.ExecuteSqlWithRetries(null, cmd, c => c.ExecuteNonQuery(), 60);
            Console.WriteLine($"Thread={thread} Completed WaitForSync.");
        }

        public static void Copy()
        {
            var tasks = new List<Task>();
            var workingTasks = 0;
            for (var i = 0; i < Threads; i++)
            {
                var thread = i;
                tasks.Add(BatchExtensions.StartTask(() =>
                {
                    Interlocked.Increment(ref workingTasks);
                    WaitForSync(thread);
                    Copy(thread);
                    Interlocked.Decrement(ref workingTasks);
                }));
            }

            Console.WriteLine($"workingTasks={workingTasks}");

            if (RunAdvanceVisibilityWatchDog)
            {
                tasks.Add(BatchExtensions.StartTask(() =>
                {
                    AdvanceVisibility(ref workingTasks);
                }));
            }

            Task.WaitAll(tasks.ToArray());
        }

        private static void AdvanceVisibility(ref int workingTasks)
        {
            var affected = 1;
            while (workingTasks > 0 || affected > 0)
            {
                Console.WriteLine($"Target.AdvanceTransactionVisibility: workingTasks={workingTasks}");
                affected = Target.AdvanceTransactionVisibility();
                if (affected == 0)
                {
                    Thread.Sleep(1000);
                }
            }
        }

        private static void Copy(int thread)
        {
            Console.WriteLine($"Copy.{thread}: started at {DateTime.UtcNow:s}");
            var transactionsPerJob = TransactionsOnly ? 10000 : 1;
            var sw = Stopwatch.StartNew();
            var resourceTypeId = (short?)0;
            while (resourceTypeId.HasValue && !stop)
            {
                var minId = 0L;
                var retries = 0;
                var maxRetries = MaxRetries;
                var version = 0L;
                var jobId = 0L;
                var resourceCount = 0;
                var totalCount = 0;
                var transactionId = new TransactionId(0);
            retry:
                try
                {
                    resourceTypeId = null;
                    Queue.DequeueJob(out jobId, out version, out var definition);
                    if (jobId != -1)
                    {
                        var split = definition.Split(";");
                        resourceTypeId = short.Parse(split[0]);
                        minId = long.Parse(split[1]);
                        var maxId = long.Parse(split[2]);
                        var suffix = split.Length > 3 ? split[3] : null;
                        if (!QueueOnly)
                        {
                            for (var t = 0; t < transactionsPerJob; t++)
                            {
                                transactionId = Target.BeginTransaction($"queuetype={SqlService.QueueType} jobid={jobId}");
                                if (!TransactionsOnly)
                                {
                                    (resourceCount, totalCount) = CopyViaSql(thread, resourceTypeId.Value, jobId, minId, maxId, transactionId);
                                }

                                Target.CommitTransaction(transactionId);
                            }
                        }

                        Queue.CompleteJob(jobId, false, version, resourceCount, totalCount);
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Copy.{thread}.{resourceTypeId}.{minId}: error={e}");
                    Queue.LogEvent($"Copy", "Error", $"{thread}.{resourceTypeId}.{minId}", text: e.ToString());
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

                    stop = true;
                    if (transactionId.Id != 0)
                    {
                        Target.CommitTransaction(transactionId, e.ToString());
                    }

                    if (jobId != -1)
                    {
                        Queue.CompleteJob(jobId, true, version);
                    }

                    throw;
                }
            }

            Console.WriteLine($"Copy.{thread}: {(stop ? "FAILED" : "completed")} at {DateTime.Now:s}, elapsed={sw.Elapsed.TotalSeconds:N0} sec.");
        }

#pragma warning disable SA1107 // Code should not contain multiple statements on one line
        private static (int resourceCnt, int totalCnt) CopyViaSql(int thread, short resourceTypeId, long jobId, long minId, long maxId, TransactionId transactionId)
        {
            var sw = Stopwatch.StartNew();
            var st = DateTime.UtcNow;
            var shardletSequence = new Dictionary<ShardletId, short>();
            var surrIdMap = new Dictionary<long, (ShardletId ShardletId, short Sequence)>(); // map from surr id to shardlet resource index
            var resources = Source.GetData(_ => new Resource(_, false), resourceTypeId, minId, maxId).ToList();

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

            resources = resources.Select(_ => { _.TransactionId = transactionId; (var shardletId, var sequence) = surrIdMap[_.ResourceSurrogateId]; _.ShardletId = shardletId; _.Sequence = sequence;  return _; }).ToList();

            var referenceSearchParams = Source.GetData(_ => new ReferenceSearchParam(_, false), resourceTypeId, minId, maxId).ToList();
            if (referenceSearchParams.Count == 0)
            {
                referenceSearchParams = null;
            }
            else
            {
                referenceSearchParams = referenceSearchParams.Where(_ => surrIdMap.ContainsKey(_.ResourceSurrogateId)).Select(_ => { _.TransactionId = transactionId; (var shardletId, var sequence) = surrIdMap[_.ResourceSurrogateId]; _.ShardletId = shardletId; _.Sequence = sequence; return _; }).ToList();
            }

            var tokenSearchParams = Source.GetData(_ => new TokenSearchParam(_, false), resourceTypeId, minId, maxId).ToList();
            if (tokenSearchParams.Count == 0)
            {
                tokenSearchParams = null;
            }
            else
            {
                tokenSearchParams = tokenSearchParams.Where(_ => surrIdMap.ContainsKey(_.ResourceSurrogateId)).Select(_ => { _.TransactionId = transactionId; (var shardletId, var sequence) = surrIdMap[_.ResourceSurrogateId]; _.ShardletId = shardletId; _.Sequence = sequence; return _; }).ToList();
            }

            var compartmentAssignments = Source.GetData(_ => new CompartmentAssignment(_, false), resourceTypeId, minId, maxId).ToList();
            if (compartmentAssignments.Count == 0)
            {
                compartmentAssignments = null;
            }
            else
            {
                compartmentAssignments = compartmentAssignments.Where(_ => surrIdMap.ContainsKey(_.ResourceSurrogateId)).Select(_ => { _.TransactionId = transactionId; (var shardletId, var sequence) = surrIdMap[_.ResourceSurrogateId]; _.ShardletId = shardletId; _.Sequence = sequence; return _; }).ToList();
            }

            var tokenTexts = Source.GetData(_ => new TokenText(_, false), resourceTypeId, minId, maxId).ToList();
            if (tokenTexts.Count == 0)
            {
                tokenTexts = null;
            }
            else
            {
                tokenTexts = tokenTexts.Where(_ => surrIdMap.ContainsKey(_.ResourceSurrogateId)).Select(_ => { _.TransactionId = transactionId; (var shardletId, var sequence) = surrIdMap[_.ResourceSurrogateId]; _.ShardletId = shardletId; _.Sequence = sequence; return _; }).ToList();
            }

            var dateTimeSearchParams = Source.GetData(_ => new DateTimeSearchParam(_, false), resourceTypeId, minId, maxId).ToList();
            if (dateTimeSearchParams.Count == 0)
            {
                dateTimeSearchParams = null;
            }
            else
            {
                dateTimeSearchParams = dateTimeSearchParams.Where(_ => surrIdMap.ContainsKey(_.ResourceSurrogateId)).Select(_ => { _.TransactionId = transactionId; (var shardletId, var sequence) = surrIdMap[_.ResourceSurrogateId]; _.ShardletId = shardletId; _.Sequence = sequence; return _; }).ToList();
            }

            var tokenQuantityCompositeSearchParams = Source.GetData(_ => new TokenQuantityCompositeSearchParam(_, false), resourceTypeId, minId, maxId).ToList();
            if (tokenQuantityCompositeSearchParams.Count == 0)
            {
                tokenQuantityCompositeSearchParams = null;
            }
            else
            {
                tokenQuantityCompositeSearchParams = tokenQuantityCompositeSearchParams.Where(_ => surrIdMap.ContainsKey(_.ResourceSurrogateId)).Select(_ => { _.TransactionId = transactionId; (var shardletId, var sequence) = surrIdMap[_.ResourceSurrogateId]; _.ShardletId = shardletId; _.Sequence = sequence; return _; }).ToList();
            }

            var quantitySearchParams = Source.GetData(_ => new QuantitySearchParam(_, false), resourceTypeId, minId, maxId).ToList();
            if (quantitySearchParams.Count == 0)
            {
                quantitySearchParams = null;
            }
            else
            {
                quantitySearchParams = quantitySearchParams.Where(_ => surrIdMap.ContainsKey(_.ResourceSurrogateId)).Select(_ => { _.TransactionId = transactionId; (var shardletId, var sequence) = surrIdMap[_.ResourceSurrogateId]; _.ShardletId = shardletId; _.Sequence = sequence; return _; }).ToList();
            }

            var stringSearchParams = Source.GetData(_ => new StringSearchParam(_, false), resourceTypeId, minId, maxId).ToList();
            if (stringSearchParams.Count == 0)
            {
                stringSearchParams = null;
            }
            else
            {
                stringSearchParams = stringSearchParams.Where(_ => surrIdMap.ContainsKey(_.ResourceSurrogateId)).Select(_ => { _.TransactionId = transactionId; (var shardletId, var sequence) = surrIdMap[_.ResourceSurrogateId]; _.ShardletId = shardletId; _.Sequence = sequence; return _; }).ToList();
            }

            var tokenTokenCompositeSearchParams = Source.GetData(_ => new TokenTokenCompositeSearchParam(_, false), resourceTypeId, minId, maxId).ToList();
            if (tokenTokenCompositeSearchParams.Count == 0)
            {
                tokenTokenCompositeSearchParams = null;
            }
            else
            {
                tokenTokenCompositeSearchParams = tokenTokenCompositeSearchParams.Where(_ => surrIdMap.ContainsKey(_.ResourceSurrogateId)).Select(_ => { _.TransactionId = transactionId; (var shardletId, var sequence) = surrIdMap[_.ResourceSurrogateId]; _.ShardletId = shardletId; _.Sequence = sequence; return _; }).ToList();
            }

            var tokenStringCompositeSearchParams = Source.GetData(_ => new TokenStringCompositeSearchParam(_, false), resourceTypeId, minId, maxId).ToList();
            if (tokenStringCompositeSearchParams.Count == 0)
            {
                tokenStringCompositeSearchParams = null;
            }
            else
            {
                tokenStringCompositeSearchParams = tokenStringCompositeSearchParams.Where(_ => surrIdMap.ContainsKey(_.ResourceSurrogateId)).Select(_ => { _.TransactionId = transactionId; (var shardletId, var sequence) = surrIdMap[_.ResourceSurrogateId]; _.ShardletId = shardletId; _.Sequence = sequence; return _; }).ToList();
            }

            var rows = 0;
            if (WritesEnabled)
            {
                rows = Target.MergeResources(transactionId, resources, referenceSearchParams, tokenSearchParams, compartmentAssignments, tokenTexts, dateTimeSearchParams, tokenQuantityCompositeSearchParams, quantitySearchParams, stringSearchParams, tokenTokenCompositeSearchParams, tokenStringCompositeSearchParams);
            }

            Console.WriteLine($"Copy.{thread}.{jobId}.{resourceTypeId}.{minId}: completed at {DateTime.Now:s}, elapsed={sw.Elapsed.TotalSeconds:N0} sec.");

            return (resources.Count, rows);
        }
#pragma warning restore SA1107 // Code should not contain multiple statements on one line

        private static void PopulateJobQueue(int unitSize)
        {
            if (Queue.JobQueueIsNotEmpty() && !RebuildQueue)
            {
                return;
            }

            var strings = Source.GetCopyUnits(unitSize);

            using var conn = new SqlConnection(Queue.ConnectionString);
            conn.Open();
            using var cmd = new SqlCommand(
                @"
EXECUTE dbo.EnqueueJobs @QueueType = @QueueType, @Definitions = @Strings, @ForceOneActiveJobGroup = 1
                ",
                conn)
            { CommandTimeout = 600 };
            cmd.Parameters.AddWithValue("@QueueType", SqlService.QueueType);
            var stringListParam = new SqlParameter { ParameterName = "@Strings" };
            stringListParam.AddStringList(strings);
            cmd.Parameters.Add(stringListParam);

            using var reader = cmd.ExecuteReader();
            var ids = new Dictionary<long, long>();
            while (reader.Read())
            {
                ids.Add(reader.GetInt64(1), reader.GetInt64(0));
            }

            Console.WriteLine($"Enqueued={ids.Count} jobs.");
        }
    }
}
