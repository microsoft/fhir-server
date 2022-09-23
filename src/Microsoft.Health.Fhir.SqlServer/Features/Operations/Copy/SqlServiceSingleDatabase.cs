// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Threading;
using Microsoft.Health.Fhir.Store.Utils;

namespace Microsoft.Health.Fhir.Store.Copy
{
    public class SqlServiceSingleDatabase : SqlUtils.SqlService
    {
        internal SqlServiceSingleDatabase(string connectionString)
            : base(connectionString)
        {
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA2100:Review SQL queries for security vulnerabilities", Justification = "No user input")]
        internal IList<T> GetData<T>(Func<SqlDataReader, T> toT, short resourceTypeId, long minId, long maxId)
        {
            while (true)
            {
                try
                {
                    var results = new List<T>();
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
                        results.Add(toT(reader));
                    }

                    return results;
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
    }
}
