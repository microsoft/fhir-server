// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Linq;
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

        public void Parse(string name, string value, ParserOptions options)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentNullException(nameof(name));
            }

            var modifier = string.Empty;
            if (name.Contains(':', StringComparison.Ordinal))
            {
                var parts = name.Split(':', 2);
                name = parts[0];
                modifier = parts[1];
            }

            var parameter = _parameterCollection.GetByCode(name, options.ResourceTypes.FirstOrDefault());
            if (parameter == null)
            {
                throw new ArgumentException($"Search Parameter '{name}' not found for resource type '{options.ResourceTypes.FirstOrDefault()}'");
            }

            var builder = options.SqlQueryBuilder;

            var surrogateIdColumn = (options.ChainLevel == 0 || options.LastCteName == null) ? "ResourceSurrogateId" : "RefResourceSurrogateId";
            var typeIdColumn = (options.ChainLevel == 0 || options.LastCteName == null) ? "ResourceTypeId" : "RefResourceTypeId";

            // Start the subquery with opening parenthesis and SELECT
            var cteName = options.ChainLevel == 0 ? $"cte{options.CteNumber}" : $"cte{options.CteNumber}chain{options.ChainLevel}";
            builder.BeginCte(cteName);
            builder.IncreaseIndent();

            // When in a chain, select the target resource columns (what we're searching against)
            if (options.ChainLevel > 0 && options.LastCteName != null)
            {
                builder.SelectWithModifier("DISTINCT", $"r.{surrogateIdColumn} AS ResourceSurrogateId", $"r.{typeIdColumn} AS ResourceTypeId");
            }
            else
            {
                builder.SelectWithModifier("DISTINCT", "r.ResourceTypeId", "r.ResourceSurrogateId");
            }

            if (modifier.Equals("missing", StringComparison.OrdinalIgnoreCase))
            {
                builder.From(options.LastCteName ?? "dbo.Resource", "r");

                var existsPrefix = bool.Parse(value) ? "NOT " : string.Empty;
                builder.Where($"{existsPrefix}EXISTS (");
                builder.IncreaseIndent();
                builder.Select("1")
                    .From(GetTableName(modifier), "t")
                    .Where($"t.ResourceSurrogateId = r.{surrogateIdColumn}")
                    .And($"t.ResourceTypeId = r.{typeIdColumn}")
                    .And($"t.SearchParamId = {parameter.Id}");
                builder.DecreaseIndent();
                builder.AppendLine(")");
            }
            else
            {
                builder.From(GetTableName(modifier), "t");

                // Join on Resource table or previous CTE
                builder.InnerJoin(
                    options.LastCteName ?? "dbo.Resource",
                    "r",
                    $"t.ResourceSurrogateId = r.{surrogateIdColumn} AND t.ResourceTypeId = r.{typeIdColumn}");

                var tableName = "t";

                if (modifier.Equals("not", StringComparison.OrdinalIgnoreCase))
                {
                    builder.Where($"NOT EXISTS (");
                    builder.IncreaseIndent();
                    builder.Select("1")
                        .From(GetTableName(modifier), "t2");
                    tableName = "t2";
                    builder.Where($"{tableName}.SearchParamId = {parameter.Id}");
                }
                else
                {
                    builder.Where($"{tableName}.SearchParamId = {parameter.Id}");
                }

                var values = SplitWithEscapeChar(value, ',', '\\');

                builder.And("(");
                builder.IncreaseIndent();

                bool firstClause = true;
                foreach (var v in values)
                {
                    var whereClause = BuildWhereClause(v, modifier, columnSuffix: null, tableName: tableName);

                    if (!firstClause)
                    {
                        builder.Or(whereClause);
                    }
                    else
                    {
                        builder.IncreaseIndent(2);
                        builder.AppendLine(whereClause);
                        builder.DecreaseIndent(2);
                    }

                    firstClause = false;
                }

                builder.DecreaseIndent();
                builder.AppendLine(")");

                if (modifier.Equals("not", StringComparison.OrdinalIgnoreCase))
                {
                    builder.And($"{tableName}.ResourceSurrogateId = t.ResourceSurrogateId");
                    builder.And($"{tableName}.ResourceTypeId = t.ResourceTypeId");
                    builder.DecreaseIndent();
                    builder.AppendLine(")");
                }
            }

            // Add base filters only on the first CTE
            if (options.LastCteName == null)
            {
                builder.And("r.IsHistory = 0");
                builder.And("r.IsDeleted = 0");

                if (options.ResourceTypes != null && options.ResourceTypes.Count > 0)
                {
                    var resourceTypeIds = string.Join(", ", options.ResourceTypes);
                    builder.And($"r.ResourceTypeId IN ({resourceTypeIds})");
                }

                if (options.ExcludedResourceTypes != null && options.ExcludedResourceTypes.Count > 0)
                {
                    var excludedResourceTypeIds = string.Join(", ", options.ExcludedResourceTypes);
                    builder.And($"r.ResourceTypeId NOT IN ({excludedResourceTypeIds})");
                }

                if (options.ContinuationToken != null)
                {
                    var surrogateOperator = options.SortDescending ? "<" : ">";
                    builder.And($"r.ResourceSurrogateId {surrogateOperator} {options.ContinuationToken.ResourceSurrogateId}");

                    if (options.ContinuationToken.ResourceTypeId != null)
                    {
                        var typeOperator = options.SortDescending ? "<" : ">";
                        builder.And($"r.ResourceTypeId {typeOperator}= {options.ContinuationToken.ResourceTypeId}");
                    }
                }
            }

            builder.DecreaseIndent();
            builder.EndCte();
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
