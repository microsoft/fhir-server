// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Text;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser
{
    public abstract class BaseSqlParser : ISqlParser
    {
        private readonly SearchParameterCollection _parameterCollection;

        protected BaseSqlParser(SearchParameterCollection parameterCollection)
        {
            ArgumentNullException.ThrowIfNull(parameterCollection);
            _parameterCollection = parameterCollection;
        }

        public string? Parse(string name, string value, ParserOptions options)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var modifier = string.Empty;
            if (name.Contains(':', StringComparison.Ordinal))
            {
                var parts = name.Split(':', 2);
                name = parts[0];
                modifier = parts[1];
            }

            var parameter = _parameterCollection.GetByCode(name, options.ResourceTypes[0]);
            if (parameter == null)
            {
                return null;
            }

            var sqlBuilder = new StringBuilder();
            sqlBuilder.AppendLine($"SELECT DISTINCT {(options.IncludeTotalCount ? string.Empty : $"TOP ({options.Count + 1})")} r.ResourceTypeId, r.ResourceSurrogateId, 1 AS IsMatch, 0 AS IsPartial, row_number() OVER (ORDER BY r.ResourceTypeId ASC, r.ResourceSurrogateId ASC) AS Row");

            if (modifier.Equals("missing", StringComparison.OrdinalIgnoreCase))
            {
                sqlBuilder.AppendLine($"  FROM {options.LastCteName ?? "dbo.Resource"} r");
                sqlBuilder.AppendLine($"  WHERE {(bool.Parse(value) ? string.Empty : "NOT ")} EXISTS (SELECT 1 FROM {parameter.Type} t WHERE t.ResourceSurrogateId = r.ResourceSurrogateId AND t.SearchParamId = {parameter.Id})");
            }
            else
            {
                sqlBuilder.AppendLine($"  FROM {parameter.Type} t");

                // Join on Resource table or previous CTE
                sqlBuilder.AppendLine($"  JOIN {options.LastCteName ?? "dbo.Resource"} r ON t.ResourceSurrogateId = r.ResourceSurrogateId");

                sqlBuilder.AppendLine($"  WHERE t.SearchParamId = {parameter.Id}");

                var values = value.Split(',');

                sqlBuilder.AppendLine("  AND (");
                var firstClause = true;
                foreach (var v in values)
                {
                    // Add parameter-specific WHERE conditions
                    var whereClause = BuildWhereClause(v, modifier);

                    if (!firstClause)
                    {
                        sqlBuilder.Append("  OR ");
                    }
                    else
                    {
                        sqlBuilder.Append("  ");
                    }

                    sqlBuilder.AppendLine(whereClause);
                    firstClause = false;
                }

                sqlBuilder.AppendLine("  )");
            }

            // Add base filters only on the first CTE
            if (options.LastCteName == null)
            {
                sqlBuilder.AppendLine($"  AND r.IsHistory = 0 AND r.IsDeleted = 0");

                if (options.ResourceTypes != null && options.ResourceTypes.Count > 0)
                {
                    var resourceTypeIds = string.Join(", ", options.ResourceTypes);
                    sqlBuilder.AppendLine($"  AND r.ResourceTypeId IN ({resourceTypeIds})");
                }

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
            }

            return sqlBuilder.ToString();
        }

        protected abstract string BuildWhereClause(string value, string modifier);

        protected static string EscapeSqlValue(string value)
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
