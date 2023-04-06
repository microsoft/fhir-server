// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    internal static class CustomQueries
    {
        private static DateTimeOffset _lastUpdatedQueryCache = DateTimeOffset.MinValue;
        public static readonly Dictionary<string, string> QueryStore = new Dictionary<string, string>();

        public static string CheckQueryHash(SqlConnection connection, string hash, ILogger<SqlServerSearchService> logger)
        {
            var now = Clock.UtcNow;
            if (now > _lastUpdatedQueryCache.AddMinutes(1))
            {
                using (SqlCommand sqlCommand = connection.CreateCommand()) // WARNING, this code will not set sqlCommand.Transaction. Sql transactions via C#/.NET are not supported in this method.
                {
                    try
                    {
                        sqlCommand.CommandText = "SELECT ROUTINE_NAME FROM INFORMATION_SCHEMA.ROUTINES WHERE ROUTINE_TYPE = 'PROCEDURE' AND ROUTINE_NAME like 'CustomQuery_%'";
                        using (SqlDataReader reader = sqlCommand.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                string sprocName = reader.GetString(0);
                                var tokens = sprocName.Split('_');
                                if (tokens.Length == 2)
                                {
                                    QueryStore.Add(tokens[1], sprocName);
                                }
                            }
                        }

                        _lastUpdatedQueryCache = now;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning("Error refreshing CustomQuery cache.  This will try again on next search execution.  Error: {ExceptionMessage}", ex.Message);
                    }
                }
            }

            QueryStore.TryGetValue(hash, out var storedProcName);
            return storedProcName;
        }
    }
}
