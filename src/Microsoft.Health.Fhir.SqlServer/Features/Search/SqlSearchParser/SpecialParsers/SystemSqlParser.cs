// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Text;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser.SpecialParsers
{
    /// <summary>
    /// Handles basic system-level searches when no search parameters are provided.
    /// Used for queries like GET [base]/Patient or GET [base]?_type=Patient
    /// </summary>
    public class SystemSqlParser : ISqlParser
    {
        public string? Parse(string name, string value, ParserOptions options)
        {
            // SystemSqlParser doesn't use name/value parameters
            // It generates a basic query based on options only
            var sqlBuilder = new StringBuilder();

            // Build the SELECT clause with TOP or without based on whether we're counting
            sqlBuilder.AppendLine($"SELECT DISTINCT {(options.IncludeTotalCount ? string.Empty : $"TOP ({options.Count + 1})")} r.ResourceTypeId, r.ResourceSurrogateId, 1 AS IsMatch, 0 AS IsPartial, row_number() OVER (ORDER BY r.ResourceTypeId ASC, r.ResourceSurrogateId ASC) AS Row");

            // FROM clause - always from dbo.Resource for system queries
            sqlBuilder.AppendLine("  FROM dbo.Resource r");

            // WHERE clause - base filters
            sqlBuilder.AppendLine("  WHERE r.IsHistory = 0 AND r.IsDeleted = 0");

            // Add resource type filter if specified
            if (options.ResourceTypes != null && options.ResourceTypes.Count > 0)
            {
                var resourceTypeIds = string.Join(", ", options.ResourceTypes);
                sqlBuilder.AppendLine();
                sqlBuilder.AppendLine($"  AND r.ResourceTypeId IN ({resourceTypeIds})");
            }

            // Add continuation token support
            if (options.ContinuationToken != null)
            {
                sqlBuilder.AppendLine($"  AND r.ResourceSurrogateId {(options.SortDescending ? "<" : ">")} {options.ContinuationToken.ResourceSurrogateId}");

                if (options.ContinuationToken.ResourceTypeId != null)
                {
                    sqlBuilder.AppendLine($"  AND r.ResourceTypeId {(options.SortDescending ? "<" : ">")}= {options.ContinuationToken.ResourceTypeId}");
                }
            }

            if (!options.IncludeTotalCount)
            {
                sqlBuilder.AppendLine($"  ORDER BY r.ResourceTypeId {(options.SortDescending ? "DESC" : "ASC")}, r.ResourceSurrogateId {(options.SortDescending ? "DESC" : "ASC")}");
            }

            return sqlBuilder.ToString();
        }
    }
}
