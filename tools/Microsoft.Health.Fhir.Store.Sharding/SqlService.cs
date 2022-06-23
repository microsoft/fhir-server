// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Microsoft.Health.Fhir.Store.Utils;

namespace Microsoft.Health.Fhir.Store.Sharding
{
    /// <summary>
    /// Handles all communication between the API and SQL Server.
    /// </summary>
    public partial class SqlService : SqlUtils.SqlService
    {
        public SqlService(string centralConnectionString)
            : base(centralConnectionString)
        {
            ShardletMap = new ShardletMap(ConnectionString, -1);
            ShardIds = ShardletMap.Shards.Keys.ToList();
        }

        public ShardletMap ShardletMap { get; private set; }

        internal int NumberOfShards => ShardletMap.Shards.Count;

        private IList<ShardId> ShardIds { get; }

        public SqlConnection GetConnection(ShardId? shardId = null, int connectionTimeoutSec = 600)
        {
            return GetConnection(shardId == null ? ConnectionString : ShardletMap.Shards[shardId.Value].ConnectionString, connectionTimeoutSec);
        }

        internal static SqlConnection GetConnection(string connectionString, int connectionTimeoutSec = 600)
        {
            var retriesSql = 0;
            var sw = Stopwatch.StartNew();
            while (true)
            {
                var connection = new SqlConnection(connectionString);
                try
                {
                    connection.Open();
                    return connection;
                }
                catch (SqlException e)
                {
                    // We have to retry the connection even if the exception is "Login failed", because
                    // SQL Azure can throw this exception when a database changes scale or physical location.
                    var prefix = $"GetConnection.[server={connection.DataSource};database={connection.Database}]: RetriesSQL={retriesSql++}: ";
                    connection.Dispose();
                    if (e.IsRetryable() || e.ToString().Contains("login failed", StringComparison.OrdinalIgnoreCase))
                    {
                        sw.Restart();
                    }
                    else if (sw.Elapsed.TotalSeconds > connectionTimeoutSec)
                    {
                        throw;
                    }

                    Thread.Sleep(5000);
                }
                catch (InvalidOperationException e)
                {
                    connection.Dispose();
                    if (!e.IsRetryable()) // not retriable
                    {
                        throw;
                    }

                    Thread.Sleep(5000);
                }
            }
        }

        internal static void ExecuteWithRetries(Action action)
        {
            while (true)
            {
                try
                {
                    action();
                    break;
                }
                catch (SqlException e)
                {
                    if (e.IsRetryable())
                    {
                        Thread.Sleep(ExceptionExtention.RetryWaitMillisecond);
                        continue;
                    }

                    throw;
                }
            }
        }

        internal void ExecuteSqlWithRetries(ShardId? shardId, SqlCommand cmd, Action<SqlCommand> action, int connectionTimeoutSec = 600)
        {
            while (true)
            {
                try
                {
                    using (var connection = GetConnection(shardId, connectionTimeoutSec))
                    {
                        cmd.Connection = connection;
                        action(cmd);
                    }

                    break;
                }
                catch (SqlException e)
                {
                    if (e.IsRetryable())
                    {
                        Thread.Sleep(ExceptionExtention.RetryWaitMillisecond);
                        continue;
                    }

                    throw;
                }
            }
        }

        internal void ExecuteSqlReaderWithRetries(ShardId? shardId, SqlCommand cmd, Action<SqlDataReader> action, int connectionTimeoutSec = 600)
        {
            ExecuteSqlWithRetries(
                    shardId,
                    cmd,
                    cmdInt =>
                        {
                            using (var reader = cmdInt.ExecuteReader())
                            {
                                action(reader);
                                reader.NextResult(); // this enables catching exception that happens after result set has been returned
                            }
                        },
                    connectionTimeoutSec);
        }

        private IDictionary<ShardId, IList<ShardletId>> GetShardedShardletIds(IEnumerable<ShardletId> shardletIds)
        {
            var shardletIdsHash = new HashSet<ShardletId>(shardletIds);
            return ShardletMap.GetShardsInfo().Item1
                        .Where(_ => shardletIdsHash.Contains(_.Key))
                        .GroupBy(_ => _.Value)
                        .ToDictionary(_ => _.Key, _ => (IList<ShardletId>)_.Select(s => s.Key).ToList());
        }

        internal ShardId GetShardId(ShardletId shardletId)
        {
            return ShardletMap.GetShardsInfo().Item1[shardletId];
        }

        ////internal IDictionary<ShardId, IList<T>> GetShardedIds<T>(IEnumerable<T> ids, Func<T, long> getId)
        ////{
        ////    var shardedList = new Dictionary<ShardId, IList<T>>();
        ////    var shardMap = ShardletMap.GetShardsInfo().Item1;
        ////    foreach (var id in ids)
        ////    {
        ////        var shardId = GetShardIdFromLong(getId(id), shardMap);
        ////        IList<T> list;
        ////        if (!shardedList.TryGetValue(shardId, out list))
        ////        {
        ////            list = new List<T>();
        ////            shardedList.Add(shardId, list);
        ////        }

        ////        list.Add(id);
        ////    }

        ////    return shardedList;
        ////}

        ////internal static ShardId GetShardIdFromLong(long id, IDictionary<ShardletId, ShardId> shardletToShardMap)
        ////{
        ////    var shardletId = new SmartId(id).ParseShardletId();
        ////    return shardletToShardMap[shardletId];
        ////}

        internal static void ForEachShard(IList<ShardId> shardIds, Action<ShardId> action)
        {
            foreach (var shardId in shardIds)
            {
                action(shardId);
            }
        }

        internal void ForEachShard(Action<ShardId> action)
        {
            var shardIds = ShardletMap.GetShardsInfo().Item2.Keys.ToList();
            ForEachShard(shardIds, action);
        }

        public static void ParallelForEachShard(IList<ShardId> shardIds, Action<ShardId> action, CancelRequest cancel)
        {
            BatchExtensions.ParallelForEach(shardIds, shardIds.Count, (thread, shardId) => { action(shardId); }, cancel);
        }

        public void ParallelForEachShard(Action<ShardId> action, CancelRequest cancel)
        {
            ParallelForEachShard(ShardIds, action, cancel);
        }
    }
}
