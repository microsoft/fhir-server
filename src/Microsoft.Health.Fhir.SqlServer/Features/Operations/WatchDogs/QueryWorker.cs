// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading;
using Microsoft.Health.Fhir.Store.Sharding;
using Microsoft.Health.Fhir.Store.Utils;

namespace Microsoft.Health.Fhir.Store.WatchDogs
{
    internal class QueryWorker
    {
        internal QueryWorker(string connStr)
        {
            SqlService = new SqlService(connStr);
            BatchExtensions.StartTask(() =>
            {
                RunQuery();
            });
        }

        public SqlService SqlService { get; private set; }

        private void RunQuery()
        {
            while (true)
            {
                if (IsEnabled())
                {
                    Thread.Sleep(10000);
                }
                else
                {
                    SqlService.LogEvent($"Query", "Warn", string.Empty, "IsEnabled=false");
                    Thread.Sleep(60000);
                }
            }
        }

        private bool IsEnabled()
        {
            using var conn = SqlService.GetConnection();
            using var cmd = new SqlCommand("SELECT convert(bit,Number) FROM dbo.Parameters WHERE Id = 'Query.IsEnabled'", conn);
            var flag = cmd.ExecuteScalar();
            return flag != null && (bool)flag;
        }

        private IList<ShardId> GetShardIds()
        {
            using var conn = SqlService.GetConnection();
            using var cmd = new SqlCommand("SELECT convert(bit,Number) FROM dbo.Parameters WHERE Id = 'Query.ShardFilter'", conn);
            var flag = cmd.ExecuteScalar();
            var filter = flag != null ? (string)flag : string.Empty;
            var shardIds = filter.Split(",").Select(_ => new ShardId(byte.Parse(_))).ToList();
            if (shardIds.Count == 0)
            {
                shardIds = SqlService.ShardIds.ToList();
            }

            return shardIds;
        }
    }
}
