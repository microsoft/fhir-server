// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Configuration;
using System.Data.SqlClient;
using System.Diagnostics;
using Microsoft.Health.Fhir.SqlServer.Database;
using SqlService = Microsoft.Health.Fhir.Store.SqlUtils.SqlService;

namespace Microsoft.Health.Fhir.IndexRebuilder
{
    public static class Program
    {
        private static readonly string ConnectionString = ConfigurationManager.ConnectionStrings["Database"].ConnectionString;
        private static readonly int Threads = int.Parse(ConfigurationManager.AppSettings["Threads"]);
        private static readonly bool RebuildClustered = bool.Parse(ConfigurationManager.AppSettings["RebuildClustered"]);
        private static readonly string EventLogQuery = ConfigurationManager.AppSettings["EventLogQuery"];
        private static readonly bool IsSharded = bool.Parse(ConfigurationManager.AppSettings["IsSharded"]);
        private static readonly string IndexToKeep = ConfigurationManager.AppSettings["IndexToKeep"];

        private const string DisableIndexesCmd = @"
DECLARE @cmd varchar(max) = ''
SELECT @cmd = @cmd + 'ALTER INDEX '+I.name+' ON '+O.name+' DISABLE'+char(10)
  FROM (SELECT * FROM sys.indexes WHERE index_id NOT IN (0,1)) I
       JOIN sys.objects O ON O.object_id = I.object_id
  WHERE EXISTS (SELECT * FROM sys.partition_schemes PS WHERE PS.data_space_id = I.data_space_id AND name = 'PartitionScheme_ResourceTypeId')
    AND I.name <> @IndexToKeep
    AND is_disabled = 0
EXECUTE(@cmd)
              ";

        public static void Main(string[] args)
        {
            Console.WriteLine($"IndexRebuilder.Start: Store(sraded={IsSharded})={SqlService.ShowConnectionString(ConnectionString)} Threads={Threads} at {DateTime.UtcNow:s}");

            var method = args.Length > 0 ? args[0] : "rebuild";
            switch (method)
            {
                case "setupdb":
                    SetupDatabase();
                    break;
                case "disable":
                    DisableIndexes();
                    break;
                case "rebuild":
                    RebuildIndexes();
                    break;
                default:
                    throw new ArgumentException("first argument on command line must be in {setupdb, disable, rebuild} (rebuild is default).");
            }
        }

        private static void SetupDatabase()
        {
            if (IsSharded)
            {
                var shardedStore = new Store.Sharding.SqlService(ConnectionString);
                foreach (var shard in shardedStore.ShardletMap.Shards)
                {
                    SetupDb.Publish(shard.Value.ConnectionString, "Microsoft.Health.Fhir.SqlServer.Database.dacpac");
                }
            }
            else
            {
                SetupDb.Publish(ConnectionString, "Microsoft.Health.Fhir.SqlServer.Database.dacpac");
            }
        }

        private static void DisableIndexes()
        {
            if (IsSharded)
            {
                var shardedStore = new Store.Sharding.SqlService(ConnectionString);
                foreach (var shard in shardedStore.ShardletMap.Shards.Values)
                {
                    DisableIndexes(shard.ConnectionString);
                }
            }
            else
            {
                DisableIndexes(ConnectionString);
            }
        }

        private static void DisableIndexes(string connectionString)
        {
            Console.WriteLine($"IndexRebuilder.DisableIndexes.Started: Store={SqlService.ShowConnectionString(connectionString)}");
            using var conn = new SqlConnection(connectionString);
            using var cmd = new SqlCommand(DisableIndexesCmd, conn);
            cmd.Parameters.AddWithValue("@IndexToKeep", IndexToKeep);
            conn.Open();
            cmd.ExecuteNonQuery();
            Console.WriteLine($"IndexRebuilder.DisableIndexes.Completed: Store={SqlService.ShowConnectionString(connectionString)}");
        }

        private static void RebuildIndexes()
        {
            var sw = Stopwatch.StartNew();
            Console.WriteLine($"To monitor progress please run in the target database(s): {EventLogQuery}");
            if (IsSharded)
            {
                var shardedStore = new Store.Sharding.SqlService(ConnectionString);
                shardedStore.ParallelForEachShard(
                    shardId =>
                    {
                        var connStr = shardedStore.ShardletMap.Shards[shardId].ConnectionString;
                        RebuildIndexes(connStr, sw);
                    },
                    null);
            }
            else
            {
                RebuildIndexes(ConnectionString, sw);
            }
        }

        private static void RebuildIndexes(string connectionString, Stopwatch sw)
        {
            var indexRebuilder = new IndexRebuilder(connectionString, Threads, RebuildClustered);
            indexRebuilder.Run(out var cancel, out var tables);
            Console.WriteLine($"IndexRebuilder.{(cancel.IsSet ? "FAILED" : "End")}: Store={SqlService.ShowConnectionString(connectionString)} Threads={Threads} Tables={tables} at {DateTime.Now:s} elapsed={sw.Elapsed.TotalSeconds:N0} sec.");
        }
    }
}
