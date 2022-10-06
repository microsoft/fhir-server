// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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
        private readonly IList<ShardId> _shardIds;

        internal QueryWorker(string connStr)
        {
            SqlService = new SqlService(connStr);
            _shardIds = GetShardIds();
            SqlService.LogEvent("Query", "Warn", string.Empty, "ShardIds", text: $"{string.Join(',', _shardIds)}.");
            BatchExtensions.StartTask(() =>
            {
                if (IsEnabled())
                {
                    RunQuery();
                }
                else
                {
                    SqlService.LogEvent($"Query", "Warn", string.Empty, "IsEnabled=false");
                }
            });
        }

        public SqlService SqlService { get; private set; }

        private void RunQuery()
        {
            while (true)
            {
            retry:
                try
                {
                    RunXShards(1);
                    RunXShards(2);
                    RunXShards(4);
                    RunXShards(8);
                    RunXShards(16);
                    RunXShards(32);
                }
                catch (Exception e)
                {
                    SqlService.LogEvent($"Query", "Error", string.Empty, text: e.ToString());
                    Thread.Sleep(10000);
                    goto retry;
                }
            }
        }

        private void RunXShards(int shards)
        {
            for (var l = 0; l < 10; l++)
            {
                if (shards == 1)
                {
                    foreach (var shardId in _shardIds) // 1 shard
                    {
                        RunQuerySingleThread(new[] { shardId });
                    }

                    return;
                }

                if (shards == 32)
                {
                    RunQuerySingleThread(_shardIds);
                    return;
                }

                IList<ShardId> shardIds = null;
                foreach (var shardId in _shardIds)
                {
                    if (shardId.Id % shards == 0)
                    {
                        shardIds = new List<ShardId>();
                    }

                    shardIds.Add(shardId);

                    if (shardId.Id % shards == shards - 1)
                    {
                        RunQuerySingleThread(shardIds);
                    }
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
            using var cmd = new SqlCommand("SELECT Char FROM dbo.Parameters WHERE Id = 'Query.ShardFilter'", conn);
            var flag = cmd.ExecuteScalar();
            var shardIds = flag != null ? ((string)flag).Split(",").Select(_ => new ShardId(byte.Parse(_))).ToList() : SqlService.ShardIds.ToList();
            return shardIds;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "No user input")]
        private void RunQuerySingleThread(IList<ShardId> shardIds, string name = "Kesha802", string code = "9279-1")
        {
            var mode = $"shards={shardIds.Count}[{string.Join(',', shardIds)}] name={name} code={code}";
            var st = DateTime.UtcNow;
            var top = 11;
            var q0 = $@"
DECLARE @p0 smallint = 1016 -- http://hl7.org/fhir/SearchParameter/Patient-name
DECLARE @p1 nvarchar(256) = '{name}' -- 281
DECLARE @p2 smallint = 103 -- Patient
DECLARE @p3 smallint = 217 -- http://hl7.org/fhir/SearchParameter/clinical-patient
DECLARE @p4 smallint = 96 -- Observation
DECLARE @p5 smallint = 202 -- http://hl7.org/fhir/SearchParameter/clinical-code
DECLARE @p6 varchar(128) = '{code}'
DECLARE @p7 int = {top}
                ";
            var q1 = $@"
DECLARE @st datetime = getUTCdate()
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
EXECUTE dbo.LogEvent @Process='Query.First',@Mode='{mode}',@Status='Warn',@Start=@st,@Rows=@@rowcount
                ";
            var q2 = $@"
DECLARE @Rows int = (SELECT count(*) FROM @ResourceKeys)

EXECUTE dbo.LogEvent @Process='Query.Second.Start',@Mode='{mode}',@Status='Warn',@Start=@CallStartTime

DECLARE @st datetime = getUTCdate()
DECLARE @ConfirmedKeys ResourceKeyList

INSERT INTO @ConfirmedKeys
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
EXECUTE dbo.LogEvent @Process='Query.Second.Insert',@Mode='{mode}',@Status='Warn',@Start=@st,@Rows=@@rowcount

SET @st = getUTCdate()
SELECT * FROM @ConfirmedKeys
EXECUTE dbo.LogEvent @Process='Query.Second.Select',@Mode='{mode}',@Status='Warn',@Start=@st,@Rows=@@rowcount
                ";

            // get resource keys from all shards
            var firstResourceKeys = new List<ResourceKey>();
            SqlService.ParallelForEachShard(
                (shardId) =>
                {
                    using var cmd = new SqlCommand(@$"{q0}{q1}") { CommandTimeout = 600 };
                    SqlService.ExecuteSqlWithRetries(
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

                            lock (firstResourceKeys)
                            {
                                firstResourceKeys.AddRange(keys);
                            }
                        });
                },
                null);

            // Check resource keys
            var checkedResourceKeys = new List<ResourceKey>();
            var callStartTime = DateTime.UtcNow;
            SqlService.ParallelForEachShard(
                shardIds,
                (shardId) =>
                {
                    using var cmd = new SqlCommand(@$"{q0}{q2}") { CommandTimeout = 600 };
                    var resourceKeysParam = new SqlParameter { ParameterName = "@ResourceKeys" };
                    resourceKeysParam.AddResourceKeyList(firstResourceKeys);
                    cmd.Parameters.AddWithValue("@CallStartTime", callStartTime);
                    cmd.Parameters.Add(resourceKeysParam);
                    SqlService.ExecuteSqlWithRetries(
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
            var final = checkedResourceKeys.GroupBy(_ => _.ResourceId).Select(_ => _.First()).OrderBy(_ => _.TransactionId.Id).ThenBy(_ => _.ShardletId.Id).ThenBy(_ => _.Sequence).Take(top).ToList();
            SqlService.LogEvent("Query", "Warn", mode, rows: checkedResourceKeys.Count, text: $"firstResourceKeys={firstResourceKeys.Count}", startTime: st);
        }
    }
}
