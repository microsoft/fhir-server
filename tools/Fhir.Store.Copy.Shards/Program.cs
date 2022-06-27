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
        private static readonly int Threads = int.Parse(ConfigurationManager.AppSettings["Threads"]);
        private static readonly int UnitSize = int.Parse(ConfigurationManager.AppSettings["UnitSize"]);
        private static readonly int MaxRetries = int.Parse(ConfigurationManager.AppSettings["MaxRetries"]);
        private static readonly bool RebuildQueue = bool.Parse(ConfigurationManager.AppSettings["RebuildQueue"]);
        private static readonly bool QueueOnly = bool.Parse(ConfigurationManager.AppSettings["QueueOnly"]);
        private static readonly bool TransactionsOnly = bool.Parse(ConfigurationManager.AppSettings["TransactionsOnly"]);
        private static readonly bool WritesEnabled = bool.Parse(ConfigurationManager.AppSettings["WritesEnabled"]);
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
            var method = args.Length > 0 ? args[0] : "merge";
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
                    DisableIndexes(shard.Key);
                }
            }
            else
            {
                Target = new SqlService(TargetConnectionString);
                Queue = new SqlService(QueueConnectionString);
                PopulateJobQueue(UnitSize);
                Copy();
            }
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

        public static void Copy()
        {
            var tasks = new List<Task>();
            for (var i = 0; i < Threads; i++)
            {
                var thread = i;
                tasks.Add(BatchExtensions.StartTask(() =>
                {
                    Copy(thread);
                }));
            }

            Task.WaitAll(tasks.ToArray());
        }

        private static void Copy(int thread)
        {
            Console.WriteLine($"Copy.{thread}: started at {DateTime.UtcNow:s}");
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
                    Queue.DequeueJob(out jobId, out version, out resourceTypeId, out minId, out var maxId);
                    if (jobId != -1)
                    {
                        if (!QueueOnly)
                        {
                            transactionId = Target.BeginTransaction($"queuetype={SqlService.QueueType} jobid={jobId}");
                            if (!TransactionsOnly)
                            {
                                (resourceCount, totalCount) = CopyViaSql(thread, resourceTypeId.Value, jobId, minId, maxId, transactionId);
                            }
                            else
                            {
                                Target.PutShardTransaction(transactionId);
                            }

                            Target.CommitTransaction(transactionId);
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
