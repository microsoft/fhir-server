// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Text;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser
{
    /// <summary>
    /// Parser for token search parameters (e.g., code, identifier, status).
    /// Token parameters can have system|code format or just code.
    /// </summary>
    public class TokenSqlParser : BaseSqlParser
    {
        public TokenSqlParser(SqlSearchParameterDefinitionManager parameterCollection)
            : base(parameterCollection)
        {
            SetTableName("TokenSearchParam");
        }

        public override string BuildWhereClause(string value, string modifier, int? columnSuffix = null, string tableName = "t")
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "1=1";
            }

            if (modifier.Equals("text", StringComparison.OrdinalIgnoreCase))
            {
                return $"({tableName}.Text LIKE N'{value.Replace("'", "''", StringComparison.Ordinal)}%')";
            }

            // Parse token value - format can be:
            // - "code" (just code)
            // - "|code" (empty system with this code)
            // - "system|code" (specific system and code)
            // - "system|" (any code in this system)

            var parts = value.Split('|', 2);
            var suffix = columnSuffix.HasValue ? columnSuffix.Value.ToString() : string.Empty;
            var conditions = new StringBuilder();

            if (parts.Length == 1)
            {
                // Just code, no system specified
                conditions.Append(BuildCodeCondition(parts[0], modifier, suffix, tableName));
            }
            else
            {
                var system = parts[0];
                var code = parts[1];

                // System is specified
                if (string.IsNullOrEmpty(system))
                {
                    conditions.Append($"({tableName}.SystemId{suffix} = (SELECT SystemId FROM dbo.System WHERE Value = '') OR {tableName}.SystemId{suffix} IS NULL)");
                }
                else
                {
                    var escapedSystem = EscapeSqlValue(system);
                    conditions.Append($"{tableName}.SystemId{suffix} = (SELECT SystemId FROM dbo.System WHERE Value = {escapedSystem})");
                }

                if (!string.IsNullOrEmpty(code))
                {
                    conditions.Append(" AND ");
                    conditions.Append(BuildCodeCondition(code, modifier, suffix, tableName));
                }
            }

            return conditions.ToString();
        }

        protected override string GetTableName(string modifier)
        {
            if (modifier.Equals("text", StringComparison.OrdinalIgnoreCase))
            {
                return "TokenText";
            }

            return "TokenSearchParam";
        }

        private static string BuildCodeCondition(string code, string modifier, string suffix, string tableName)
        {
            const int MaxCodeLength = 256;

            if (code.Length <= MaxCodeLength)
            {
                // Code fits in the Code column
                var escapedCode = EscapeSqlValue(code);
                return $"{tableName}.Code{suffix} = {escapedCode}";
            }
            else
            {
                // Code is longer than 256 characters
                // The first 256 characters are in Code, the rest in CodeOverflow
                var codePrefix = code.Substring(0, MaxCodeLength);
                var codeOverflow = code.Substring(MaxCodeLength);

                var escapedPrefix = EscapeSqlValue(codePrefix);
                var escapedOverflow = EscapeSqlValue(codeOverflow);

                return $"({tableName}.Code{suffix} = {escapedPrefix} AND {tableName}.CodeOverflow{suffix} = {escapedOverflow})";
            }
        }
    }
}
