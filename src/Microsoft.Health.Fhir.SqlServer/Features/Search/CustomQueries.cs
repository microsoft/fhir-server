// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    internal static class CustomQueries
    {
        private static DateTimeOffset _lastUpdatedQueryCache = DateTimeOffset.MinValue;
        private static object lockObject = new object();
        public static readonly ConcurrentDictionary<string, string> QueryStore = new ConcurrentDictionary<string, string>();

        public static int WaitTime { get; set; } = 60;

        public static string CheckQueryHash(IDbConnection connection, string hash, ILogger<SqlServerSearchService> logger)
        {
            var now = Clock.UtcNow;
            if (now > _lastUpdatedQueryCache.AddSeconds(WaitTime))
            {
                lock (lockObject)
                {
                    if (now > _lastUpdatedQueryCache.AddSeconds(WaitTime))
                    {
                        using (IDbCommand sqlCommand = connection.CreateCommand())
                        {
                            try
                            {
                                var tempQueryStore = new Dictionary<string, string>();
                                sqlCommand.CommandText = "SELECT name FROM sys.objects WHERE type = 'p' AND name LIKE 'CustomQuery[_]%'";
                                using (IDataReader reader = sqlCommand.ExecuteReader())
                                {
                                    while (reader.Read())
                                    {
                                        string sprocName = reader.GetString(0);
                                        var tokens = sprocName.Split('_');
                                        if (tokens.Length == 2)
                                        {
                                            tempQueryStore.TryAdd(tokens[1], sprocName);
                                        }
                                    }
                                }

                                QueryStore.Clear();
                                foreach (var item in tempQueryStore)
                                {
                                    QueryStore.TryAdd(item.Key, item.Value);
                                }

                                _lastUpdatedQueryCache = now;
                            }
                            catch (Exception ex)
                            {
                                logger.LogWarning("Error refreshing CustomQuery cache.  This will try again on next search execution.  Error: {ExceptionMessage}", ex.Message);
                            }
                        }
                    }
                }
            }

            QueryStore.TryGetValue(hash, out var storedProcName);
            return storedProcName;
        }
    }
}
