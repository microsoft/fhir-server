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
        private readonly SqlSearchParameterDefinitionManager _parameterCollection;
        private string _tableName = string.Empty;

        protected BaseSqlParser(SqlSearchParameterDefinitionManager parameterCollection)
        {
            ArgumentNullException.ThrowIfNull(parameterCollection);
            _parameterCollection = parameterCollection;
        }

        protected virtual string GetTableName(string modifier)
        {
            return _tableName;
        }

        protected void SetTableName(string value)
        {
            _tableName = value;
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
            sqlBuilder.AppendLine($"SELECT *, row_number() OVER (ORDER BY ResourceTypeId ASC, ResourceSurrogateId ASC) AS Row");
            sqlBuilder.AppendLine($"  FROM (SELECT DISTINCT r.ResourceTypeId, r.ResourceSurrogateId, 1 AS IsMatch, 0 AS IsPartial");

            var surrogateIdColumn = (options.ChainLevel == 0 || options.LastCteName == null) ? "ResourceSurrogateId" : "RefResourceSurrogateId";

            if (modifier.Equals("missing", StringComparison.OrdinalIgnoreCase))
            {
                sqlBuilder.AppendLine($"  FROM {options.LastCteName ?? "dbo.Resource"} r");
                sqlBuilder.AppendLine($"  WHERE {(bool.Parse(value) ? "NOT " : string.Empty)}EXISTS (SELECT 1 FROM {GetTableName(modifier)} t WHERE t.ResourceSurrogateId = r.{surrogateIdColumn} AND t.SearchParamId = {parameter.Id})");
            }
            else
            {
                sqlBuilder.AppendLine($"  FROM {GetTableName(modifier)} t");

                // Join on Resource table or previous CTE
                sqlBuilder.AppendLine($"  JOIN {options.LastCteName ?? "dbo.Resource"} r ON t.ResourceSurrogateId = r.{surrogateIdColumn}");

                var tableName = "t";

                if (modifier.Equals("not", StringComparison.OrdinalIgnoreCase))
                {
                    sqlBuilder.AppendLine($"  WHERE NOT EXISTS (SELECT 1 FROM {GetTableName(modifier)} t2");
                    tableName = "t2";
                }

                sqlBuilder.AppendLine($"  WHERE {tableName}.SearchParamId = {parameter.Id}");

                var values = SplitWithEscapeChar(value, ',', '\\');

                sqlBuilder.AppendLine("  AND (");
                var firstClause = true;
                foreach (var v in values)
                {
                    // Add parameter-specific WHERE conditions
                    var whereClause = BuildWhereClause(v, modifier, columnSuffix: null, tableName: tableName);

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

                if (modifier.Equals("not", StringComparison.OrdinalIgnoreCase))
                {
                    sqlBuilder.AppendLine($"  AND {tableName}.ResourceSurrogateId = t.ResourceSurrogateId");
                    sqlBuilder.AppendLine($"  AND {tableName}.ResourceTypeId = t.ResourceTypeId");
                    sqlBuilder.AppendLine("  )");
                }
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

                if (options.ExcludedResourceTypes != null && options.ExcludedResourceTypes.Count > 0)
                {
                    var excludedResourceTypeIds = string.Join(", ", options.ExcludedResourceTypes);
                    sqlBuilder.AppendLine($"  AND r.ResourceTypeId NOT IN ({excludedResourceTypeIds})");
                }

                if (options.ContinuationToken != null)
                {
                    sqlBuilder.AppendLine($"  AND r.ResourceSurrogateId {(options.SortDescending ? "<" : ">")} {options.ContinuationToken.ResourceSurrogateId}");

                    if (options.ContinuationToken.ResourceTypeId != null)
                    {
                        sqlBuilder.AppendLine($"  AND r.ResourceTypeId {(options.SortDescending ? "<" : ">")}= {options.ContinuationToken.ResourceTypeId}");
                    }
                }
            }

            sqlBuilder.Append("  ) as a");

            return sqlBuilder.ToString();
        }

        /// <summary>
        /// Builds the WHERE clause for the search parameter.
        /// </summary>
        /// <param name="value">The search value.</param>
        /// <param name="modifier">The search modifier (if any).</param>
        /// <param name="columnSuffix">Optional numeric suffix for column names in composite tables (e.g., 2 for "Text2"). Null for non-composite tables.</param>
        /// <param name="tableName">The table name or alias to use in the WHERE clause.</param>
        /// <returns>The SQL WHERE clause.</returns>
        public abstract string BuildWhereClause(string value, string modifier, int? columnSuffix = null, string tableName = "t");

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

        private static string[] SplitWithEscapeChar(string value, char separator, char escapeChar)
        {
            var result = new System.Collections.Generic.List<string>();
            var current = new StringBuilder();
            bool isEscaped = false;
            foreach (var c in value)
            {
                if (isEscaped)
                {
                    current.Append(c);
                    isEscaped = false;
                }
                else if (c == escapeChar)
                {
                    isEscaped = true;
                }
                else if (c == separator)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            result.Add(current.ToString());
            return result.ToArray();
        }
    }
}
