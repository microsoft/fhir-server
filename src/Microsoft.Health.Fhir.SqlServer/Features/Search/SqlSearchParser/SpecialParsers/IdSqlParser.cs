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
        public void Parse(string name, string value, ParserOptions options)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(value));
            }

            var parameterParts = name.Split(':');
            var modifier = parameterParts.Length > 1 ? parameterParts[1] : string.Empty;

            // Handle comma-separated list of IDs (e.g., _id=123,456,789)
            var ids = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (ids.Length == 0)
            {
                throw new ArgumentException("No valid IDs provided.", nameof(value));
            }

            var sqlBuilder = options.SqlQueryBuilder;
            var cteName = options.ChainLevel == 0 ? $"cte{options.CteNumber}" : $"cte{options.CteNumber}chain{options.ChainLevel}";
            options.ResultCteName = cteName;

            var surrogateIdColumn = (options.ChainLevel == 0 || options.LastCteName == null) ? "ResourceSurrogateId" : "RefResourceSurrogateId";
            var typeIdColumn = (options.ChainLevel == 0 || options.LastCteName == null) ? "ResourceTypeId" : "RefResourceTypeId";

            sqlBuilder.BeginCte(cteName);
            sqlBuilder.SelectWithModifier("DISTINCT", $"r.ResourceTypeId AS {typeIdColumn}", $"r.ResourceSurrogateId AS {surrogateIdColumn}");
            sqlBuilder.From("dbo.Resource", "r");

            if (options.LastCteName != null)
            {
                sqlBuilder.JoinMultiLine("INNER", options.LastCteName, "lcte", $"r.ResourceSurrogateId = lcte.{surrogateIdColumn}", $"r.ResourceTypeId = lcte.{typeIdColumn}");
            }

            // Build WHERE clause for ResourceId matching
            if (ids.Length == 1)
            {
                var escapedId = EscapeSqlValue(ids[0]);
                sqlBuilder.Where($"r.ResourceId {(modifier.Equals("not", StringComparison.OrdinalIgnoreCase) ? "<>" : "=")} {escapedId}");
            }
            else
            {
                // Multiple IDs - use IN clause
                var escapedIds = string.Join(", ", ids.Select(EscapeSqlValue));
                sqlBuilder.Where($"r.ResourceId {(modifier.Equals("not", StringComparison.OrdinalIgnoreCase) ? "NOT IN" : "IN")} ({escapedIds})");
            }

            // Add base filters only on the first CTE
            ParserUtil.AddFirstCteFilters(sqlBuilder, options, "r");

            sqlBuilder.EndCte();
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
