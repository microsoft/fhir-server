// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data.SqlClient;
using System.Threading;
using Microsoft.Health.Fhir.Store.Sharding;
using Microsoft.Health.Fhir.Store.Utils;

namespace Microsoft.Health.Fhir.Store.WatchDogs
{
    internal class IndexRebuildWorker
    {
        private int _threads;

        internal IndexRebuildWorker(string connStr)
        {
            SqlService = new SqlService(connStr);
            BatchExtensions.StartTask(() =>
            {
                RebuildIndexes();
            });
        }

        public SqlService SqlService { get; private set; }

        private void RebuildIndexes()
        {
            while (true)
            {
                if (IsEnabled())
                {
                    _threads = GetThreads();
                    SqlService.LogEvent($"RebuildIndexes", "Warn", string.Empty, $"Threads={_threads}");
                    SqlService.ParallelForEachShard(
                        shardId =>
                        {
                            var connStr = SqlService.ShardletMap.Shards[shardId].ConnectionString;
                            SqlService.LogEvent($"RebuildIndexes.Start", "Warn", string.Empty, SqlService.ShowConnectionString(connStr));
                            RebuildIndexes(connStr);
                        },
                        null);
                }
                else
                {
                    SqlService.LogEvent($"RebuildIndexes", "Warn", string.Empty, "IsEnabled=false");
                    Thread.Sleep(60000);
                }
            }
        }

        private void RebuildIndexes(string connectionString)
        {
            retry:
            try
            {
                var indexRebuilder = new IndexRebuilder(connectionString, _threads, false);
                indexRebuilder.Run(out var cancel, out var tables);
                Thread.Sleep(60000);
            }
            catch (SqlException e)
            {
                SqlService.LogEvent($"RebuildIndexes", "Error", SqlService.ShowConnectionString(connectionString), text: e.ToString());
                Thread.Sleep(60000);
                goto retry;
            }
        }

        private int GetThreads()
        {
            using var conn = SqlService.GetConnection();
            using var cmd = new SqlCommand("SELECT convert(int,Number) FROM dbo.Parameters WHERE Id = 'IndexRebuild.Threads'", conn);
            var threads = cmd.ExecuteScalar();
            return threads == null ? 1 : (int)threads;
        }

        private bool IsEnabled()
        {
            using var conn = SqlService.GetConnection();
            using var cmd = new SqlCommand("SELECT convert(bit,Number) FROM dbo.Parameters WHERE Id = 'IndexRebuild.IsEnabled'", conn);
            var flag = cmd.ExecuteScalar();
            return flag != null && (bool)flag;
        }
    }
}
