// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser
{
    public static class QueryStringParser
    {
        public static Dictionary<string, List<string>> Parse(string queryString)
        {
            var parameters = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(queryString))
            {
                return parameters;
            }

            // Extract the query part after the '?'
            var queryIndex = queryString.IndexOf('?', StringComparison.Ordinal);
            if (queryIndex == -1)
            {
                return parameters;
            }

            var queryPart = queryString.Substring(queryIndex + 1);

            // Split by '&' to get individual parameters
            var paramPairs = queryPart.Split('&', StringSplitOptions.RemoveEmptyEntries);

            foreach (var pair in paramPairs)
            {
                var equalIndex = pair.IndexOf('=', StringComparison.Ordinal);

                string key, value;
                if (equalIndex == -1)
                {
                    // Parameter without value
                    key = Uri.UnescapeDataString(pair);
                    value = string.Empty;
                }
                else
                {
                    key = Uri.UnescapeDataString(pair[..equalIndex]);
                    value = Uri.UnescapeDataString(pair[(equalIndex + 1)..]);
                }

                // Add to dictionary, handling multiple values for the same key
                if (!parameters.TryGetValue(key, out var list))
                {
                    list = new List<string>();
                    parameters[key] = list;
                }

                list.Add(value);
            }

            return parameters;
        }
    }
}
