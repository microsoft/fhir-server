// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Store.Copy
{
    internal class SqlServiceSingleDatabase : SqlUtils.SqlService
    {
        internal SqlServiceSingleDatabase(string connectionString)
            : base(connectionString)
        {
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "No user input")]
        internal IEnumerable<T> GetData<T>(Func<SqlDataReader, T> toT, short resourceTypeId, long minId, long maxId)
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = new SqlCommand(
                @$"
SELECT * FROM dbo.{typeof(T).Name} WHERE ResourceTypeId = @ResourceTypeId AND ResourceSurrogateId BETWEEN @MinId AND @MaxId ORDER BY ResourceSurrogateId",
                conn)
            { CommandTimeout = 600 };
            cmd.Parameters.AddWithValue("@ResourceTypeId", resourceTypeId);
            cmd.Parameters.AddWithValue("@MinId", minId);
            cmd.Parameters.AddWithValue("@MaxId", maxId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                yield return toT(reader);
            }
        }

        internal IList<short> GetResourceTypeIds()
        {
            using var conn = new SqlConnection(ConnectionString);
            conn.Open();
            using var cmd = new SqlCommand("SELECT ResourceTypeId FROM dbo.ResourceType", conn) { CommandTimeout = 60 };
            using var reader = cmd.ExecuteReader();
            var results = new List<short>();
            while (reader.Read())
            {
                results.Add(reader.GetInt16(0));
            }

            return results;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "No user input")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Not used for security")]
        internal IList<string> GetCopyUnits(int unitSize)
        {
            var resourceTypeIds = GetResourceTypeIds();
            var strings = new List<string>();
            Parallel.ForEach(
                resourceTypeIds,
                new ParallelOptions { MaxDegreeOfParallelism = 4 },
                (resourceTypeId) =>
                {
                    using var sourceConn = new SqlConnection(ConnectionString);
                    sourceConn.Open();
                    var sql = @"
SELECT convert(varchar,ResourceTypeId)+';'+convert(varchar,MinId)+';'+convert(varchar,MaxId)
  FROM (SELECT ResourceTypeId
              ,MinId = min(ResourceSurrogateId)
              ,MaxId = max(ResourceSurrogateId)
          FROM (SELECT PartUnitId = isnull(convert(int, (row_number() OVER (ORDER BY ResourceSurrogateId) - 1) / @UnitSize), 0)
                      ,ResourceTypeId
                      ,ResourceSurrogateId
                  FROM dbo.Resource
                  WHERE ResourceTypeId = @ResourceTypeId
               ) A
          GROUP BY
               ResourceTypeId
              ,PartUnitId
       ) A
  OPTION (MAXDOP 8, RECOMPILE)
                                ";
                    using var sourceCommand = new SqlCommand(sql, sourceConn) { CommandTimeout = 7200 }; // this takes 30 minutes on db with 2B resources
                    sourceCommand.Parameters.AddWithValue("@UnitSize", unitSize);
                    sourceCommand.Parameters.AddWithValue("@ResourceTypeId", resourceTypeId);
                    using var reader = sourceCommand.ExecuteReader();
                    var strInt = new List<string>();
                    while (reader.Read())
                    {
                        strInt.Add(reader.GetString(0));
                    }

                    lock (strings)
                    {
                        strings.AddRange(strInt);
                    }

                    Console.WriteLine($"ResourceType={resourceTypeId} UnitSize={unitSize} Ranges={strInt.Count}");
                });

            var rand = new Random();
            return strings.OrderBy(_ => rand.NextDouble()).ToList();
        }
    }
}
