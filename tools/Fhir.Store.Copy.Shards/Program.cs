// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data.SqlClient;
using System.Threading;
using Microsoft.Health.Fhir.SqlServer.Database;
using Microsoft.Health.Fhir.Store.Sharding;

namespace Microsoft.Health.Fhir.Store.Copy
{
    public static class Program
    {
        private static readonly string SourceConnectionString = ConfigurationManager.ConnectionStrings["SourceDatabase"].ConnectionString;
        private static readonly string TargetConnectionString = ConfigurationManager.ConnectionStrings["TargetDatabase"].ConnectionString;
        private static readonly string QueueConnectionString = ConfigurationManager.ConnectionStrings["QueueDatabase"].ConnectionString;
        private static readonly int UnitSize = int.Parse(ConfigurationManager.AppSettings["UnitSize"]);
        private static readonly bool RebuildQueue = bool.Parse(ConfigurationManager.AppSettings["RebuildQueue"]);
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
                SetupDb.Publish(TargetConnectionString, "Microsoft.Health.Fhir.SqlServer.Database.dacpac");
                Target = new SqlService(TargetConnectionString);
                foreach (var shard in Target.ShardletMap.Shards)
                {
                    SetupDb.Publish(shard.Value.ConnectionString, "Microsoft.Health.Fhir.SqlServer.Database.Distributed.dacpac");
                    SetupDb.Publish(shard.Value.ConnectionString, "Microsoft.Health.Fhir.SqlServer.Database.dacpac");
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
                ////PopulateJobQueue(UnitSize);
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
            var qw = new WatchDogs.QueryWorker(TargetConnectionString);
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
            var cw = new WatchDogs.CopyWorker(TargetConnectionString);
            while (true)
            {
                Thread.Sleep(1000);
            }
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
            cmd.Parameters.AddWithValue("@QueueType", SqlService.CopyQueueType);
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
