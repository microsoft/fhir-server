// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
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

        public IList<ShardId> ShardIds { get; }

        public SqlConnection GetConnection(ShardId? shardId = null, int connectionTimeoutSec = 600)
        {
            return GetConnection(shardId == null ? ConnectionString : ShardletMap.Shards[shardId.Value].ConnectionString, connectionTimeoutSec);
        }

        public string GetConnectionSring(ShardId? shardId = null)
        {
            return shardId == null ? ConnectionString : ShardletMap.Shards[shardId.Value].ConnectionString;
        }

        public void ExecuteSqlWithRetries(ShardId? shardId, SqlCommand cmd, Action<SqlCommand> action, int connectionTimeoutSec = 600)
        {
            var connStr = GetConnectionSring(shardId);
            ExecuteSqlWithRetries(connStr, cmd, action, connectionTimeoutSec);
        }

        internal void ExecuteSqlReaderWithRetries(ShardId? shardId, SqlCommand cmd, Action<SqlDataReader> action, int connectionTimeoutSec = 600)
        {
            var connStr = GetConnectionSring(shardId);
            ExecuteSqlReaderWithRetries(connStr, cmd, action, connectionTimeoutSec);
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
