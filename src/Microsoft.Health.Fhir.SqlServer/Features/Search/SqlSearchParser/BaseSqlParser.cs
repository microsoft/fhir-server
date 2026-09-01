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
            options.ResultCteName = cteName;
            builder.BeginCte(cteName);
            builder.IncreaseIndent();

            // When in a chain, select the target resource columns (what we're searching against)
            builder.SelectWithModifier("DISTINCT", $"r.{typeIdColumn}", $"r.{surrogateIdColumn}");

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

                builder.IncreaseIndent();
                builder.AppendLine(")");
                builder.DecreaseIndent(2);

                if (modifier.Equals("not", StringComparison.OrdinalIgnoreCase))
                {
                    builder.And($"{tableName}.ResourceSurrogateId = t.ResourceSurrogateId");
                    builder.And($"{tableName}.ResourceTypeId = t.ResourceTypeId");
                    builder.DecreaseIndent();
                    builder.AppendLine(")");
                }
            }

            // Add base filters only on the first CTE
            ParserUtil.AddFirstCteFilters(builder, options, "r");

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

        /// <summary>
        /// Returns the search table name, search param ID, and WHERE clause for use in a combined CTE.
        /// This avoids creating a separate CTE for each search condition when multiple conditions
        /// target the same resource type (e.g., in reverse chain groups).
        /// </summary>
        public (string tableName, int searchParamId, string whereClause)? GetSearchJoinInfo(
            string name,
            string value,
            short resourceTypeId)
        {
            var modifier = string.Empty;
            if (name.Contains(':', StringComparison.Ordinal))
            {
                var parts = name.Split(':', 2);
                name = parts[0];
                modifier = parts[1];
            }

            // :missing and :not modifiers need full CTE — can't be combined
            if (modifier.Equals("missing", StringComparison.OrdinalIgnoreCase) ||
                modifier.Equals("not", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var parameter = _parameterCollection.GetByCode(name, resourceTypeId);
            if (parameter == null)
            {
                return null;
            }

            var tableName = GetTableName(modifier);
            var values = SplitWithEscapeChar(value, ',', '\\');

            var whereParts = new StringBuilder();
            bool first = true;
            foreach (var v in values)
            {
                var clause = BuildWhereClause(v, modifier, columnSuffix: null, tableName: "t_placeholder");
                if (!first)
                {
                    whereParts.Append(" OR ");
                }

                whereParts.Append(clause);
                first = false;
            }

            var whereClause = values.Length > 1 ? $"({whereParts})" : whereParts.ToString();

            return (tableName, parameter.Id, whereClause);
        }

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
