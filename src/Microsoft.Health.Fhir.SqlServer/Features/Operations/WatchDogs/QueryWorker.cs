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
    public class QueryWorker
    {
        private readonly IList<ShardId> _shardIds;

        public QueryWorker(string connStr)
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
                    RunAllShards();
                }
                catch (Exception e)
                {
                    SqlService.LogEvent($"Query", "Error", string.Empty, text: e.ToString());
                    Thread.Sleep(10000);
                    goto retry;
                }
            }
        }

        private void RunAllShards()
        {
            for (var l = 0; l < 10; l++)
            {
                RunQuerySingleThread(_shardIds);
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
                }
                else if (shards == 32)
                {
                    RunQuerySingleThread(_shardIds);
                }
                else
                {
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

        private void RunQuerySingleThread(IList<ShardId> shardIds, string name = "Kesha802", string code = "9279-1")
        {
            var mode = $"shards={shardIds.Count}[{string.Join(',', shardIds)}] name={name} code={code}";
            var st = DateTime.UtcNow;

            var top = 11;
            var queryParams = $@"
DECLARE @p0 smallint = 1016 -- http://hl7.org/fhir/SearchParameter/Patient-name
DECLARE @p1 nvarchar(256) = '{name}' -- 281
DECLARE @p2 smallint = 103 -- Patient
DECLARE @p3 smallint = 217 -- http://hl7.org/fhir/SearchParameter/clinical-patient
DECLARE @p4 smallint = 96 -- Observation
DECLARE @p5 smallint = 202 -- http://hl7.org/fhir/SearchParameter/clinical-code
DECLARE @p6 varchar(128) = '{code}'
DECLARE @p7 int = {top}
                ";
            var query1 = $@"
DECLARE @st datetime = getUTCdate()

EXECUTE sp_executeSQL 
N'
SELECT Patient.*
  FROM (SELECT ResourceTypeId, ResourceId, TransactionId, ShardletId, Sequence
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
       ) Patient
       CROSS APPLY (SELECT TOP 1 TransactionId
                      FROM dbo.Resource WITH (FORCESEEK)
                      WHERE IsHistory = 0
                        AND ResourceTypeId = @p2
                        AND ResourceId = Patient.ResourceId
                        AND TransactionId <= 40000000000
                      ORDER BY TransactionId DESC, ShardletId DESC, Sequence DESC
                    ) Curr
  WHERE Patient.TransactionId - Curr.TransactionId = 0 -- minus is to guarantee that where clause is used as filtering condition only 
'
 ,N'@p0 smallint, @p1 nvarchar(256), @p2 smallint, @p3 smallint, @p4 smallint, @p5 smallint, @p6 varchar(128)'
 ,@p0 = @p0
 ,@p1 = @p1
 ,@p2 = @p2
 ,@p3 = @p3
 ,@p4 = @p4
 ,@p5 = @p5
 ,@p6 = @p6

EXECUTE dbo.LogEvent @Process='Query.First',@Mode='{mode}',@Status='Warn',@Start=@st,@Rows=@@rowcount
                ";
            var query2 = $@"
DECLARE @Rows int = (SELECT count(*) FROM @ResourceKeys)
EXECUTE dbo.LogEvent @Process='Query.Second.Start',@Mode='{mode}',@Status='Warn',@Start=@CallStartTime,@Rows=@Rows

DECLARE @st datetime = getUTCdate()
EXECUTE sp_executeSQL 
N'
DECLARE @DummyTop bigint = convert(bigint,9223372036854775807)

SELECT TOP (@p7) ResourceTypeId, ResourceId, TransactionId, ShardletId, Sequence 
  FROM (SELECT TOP (@DummyTop) * FROM @ResourceKeys) Patient
  WHERE EXISTS 
          (SELECT *
             FROM (SELECT *
                     FROM dbo.ReferenceSearchParam Observ
                     WHERE Observ.IsHistory = 0
                       AND Observ.ResourceTypeId = @p4 -- Observation
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
                  ) Observ
                  JOIN dbo.Resource Res ON Res.IsHistory = 0 AND Res.ResourceTypeId = @p4 AND Res.TransactionId = Observ.TransactionId AND Res.ShardletId = Observ.ShardletId AND Res.Sequence = Observ.Sequence
                  CROSS APPLY (SELECT TOP 1 TransactionId, ShardletId, Sequence
                                 FROM dbo.Resource
                                 WHERE IsHistory = 0
                                   AND ResourceTypeId = @p4
                                   AND ResourceId = Res.ResourceId
                                   AND TransactionId <= 40000000000
                                 ORDER BY TransactionId DESC, ShardletId DESC, Sequence DESC
                               ) Curr
             WHERE Observ.TransactionId - Curr.TransactionId = 0 -- minus is to guarantee that where clause is used as filtering condition only 
          )
  ORDER BY TransactionId, ShardletId, Sequence 
  OPTION (OPTIMIZE FOR (@DummyTop = 1))
  --OPTION (RECOMPILE)
 '
 ,N'@p0 smallint, @p1 nvarchar(256), @p2 smallint, @p3 smallint, @p4 smallint, @p5 smallint, @p6 varchar(128), @p7 int, @ResourceKeys ResourceKeyList READONLY'
 ,@p0 = @p0
 ,@p1 = @p1
 ,@p2 = @p2
 ,@p3 = @p3
 ,@p4 = @p4
 ,@p5 = @p5
 ,@p6 = @p6
 ,@p7 = @p7
 ,@ResourceKeys = @ResourceKeys

EXECUTE dbo.LogEvent @Process='Query.Second.End',@Mode='{mode}',@Status='Warn',@Start=@st,@Rows=@@rowcount
                ";

            var q1ResourceKeys = GetRecourceKeysFanOut($"{queryParams}{query1}", null);

            var q2ResourceKeys = GetRecourceKeysFanOut($"{queryParams}{query2}", q1ResourceKeys);

            var finalResourceKeys = q2ResourceKeys.GroupBy(_ => _.ResourceId).Select(_ => _.First()).OrderBy(_ => _.TransactionId.Id).ThenBy(_ => _.ShardletId.Id).ThenBy(_ => _.Sequence).Take(top).ToList();

            SqlService.LogEvent("Query", "Warn", mode, rows: q2ResourceKeys.Count, text: $"firstResourceKeys={q1ResourceKeys.Count}", startTime: st);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "No user input")]
        private IList<ResourceKey> GetRecourceKeysFanOut(string query, IList<ResourceKey> keys)
        {
            var callStartTime = DateTime.UtcNow;
            var resourceKeys = new List<ResourceKey>();
            if (keys != null && keys.Count == 0)
            {
                return resourceKeys;
            }

            SqlService.ParallelForEachShard(
                (shardId) =>
                {
                    using var cmd = new SqlCommand(query) { CommandTimeout = 600 };
                    cmd.Parameters.AddWithValue("@CallStartTime", callStartTime); // for debugging only. can be removed
                    if (keys != null && keys.Count > 0)
                    {
                        var resourceKeysParam = new SqlParameter { ParameterName = "@ResourceKeys" };
                        resourceKeysParam.AddResourceKeyList(keys);
                        cmd.Parameters.Add(resourceKeysParam);
                    }

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

                            lock (resourceKeys)
                            {
                                resourceKeys.AddRange(keys);
                            }
                        });
                },
                null);
            return resourceKeys;
        }
    }
}
