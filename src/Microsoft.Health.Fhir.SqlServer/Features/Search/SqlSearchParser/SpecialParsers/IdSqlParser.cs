// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Linq;
using System.Text;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser.SpecialParsers
{
    /// <summary>
    /// Parser for the _id search parameter.
    /// Searches directly on the Resource table's ResourceId column.
    /// </summary>
    public class IdSqlParser : ISqlParser
    {
        public string? Parse(string name, string value, ParserOptions options)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            // Handle comma-separated list of IDs (e.g., _id=123,456,789)
            var ids = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (ids.Length == 0)
            {
                return null;
            }

            var sqlBuilder = new StringBuilder();
            sqlBuilder.AppendLine($"SELECT DISTINCT {(options.IncludeTotalCount ? string.Empty : $"TOP ({options.Count + 1})")} r.ResourceTypeId, r.ResourceSurrogateId, 1 AS IsMatch, 0 AS IsPartial, row_number() OVER (ORDER BY r.ResourceTypeId ASC, r.ResourceSurrogateId ASC) AS Row");
            sqlBuilder.AppendLine($"  FROM {options.LastCteName ?? "dbo.Resource"} r");

            // Build WHERE clause for ResourceId matching
            if (ids.Length == 1)
            {
                var escapedId = EscapeSqlValue(ids[0]);
                sqlBuilder.AppendLine($"  WHERE r.ResourceId = {escapedId}");
            }
            else
            {
                // Multiple IDs - use IN clause
                var escapedIds = string.Join(", ", ids.Select(EscapeSqlValue));
                sqlBuilder.AppendLine($"  WHERE r.ResourceId IN ({escapedIds})");
            }

            // Add base filters only on the first CTE
            if (options.LastCteName == null)
            {
                sqlBuilder.AppendLine("  AND r.IsHistory = 0 AND r.IsDeleted = 0");

                if (options.ResourceTypes != null && options.ResourceTypes.Count > 0)
                {
                    var resourceTypeIds = string.Join(", ", options.ResourceTypes);
                    sqlBuilder.AppendLine($"  AND r.ResourceTypeId IN ({resourceTypeIds})");
                }

                if (options.ContinuationSurrogateId.HasValue)
                {
                    sqlBuilder.Append($"  AND r.ResourceSurrogateId > {options.ContinuationSurrogateId.Value}");
                }
            }

            return sqlBuilder.ToString();
        }

        private static string EscapeSqlValue(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "''";
            }

            // Escape single quotes by doubling them
            var escaped = value.Replace("'", "''", StringComparison.Ordinal);
            return $"'{escaped}'";
        }
    }
}
