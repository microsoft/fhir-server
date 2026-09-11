// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

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

        public override string BuildWhereClause(string value, string modifier, ParserOptions options, int? columnSuffix = null, string tableName = "t")
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "1=1";
            }

            if (string.Equals(modifier, "text", StringComparison.OrdinalIgnoreCase))
            {
                var textParameter = options.AddParameter(VLatest.TokenText.Text, $"{value}%", includeInHash: true);
                return $"({tableName}.Text LIKE {textParameter})";
            }

            // Parse token value - format can be:
            // - "code" (just code)
            // - "|code" (empty system with this code)
            // - "system|code" (specific system and code)
            // - "system|" (any code in this system)

            var parts = value.Split('|', 2);
            var suffix = columnSuffix.HasValue ? columnSuffix.Value.ToString() : string.Empty;

            if (parts.Length == 1)
            {
                // Just code, no system specified
                return BuildCodeCondition(parts[0], options, suffix, tableName);
            }

            var system = parts[0];
            var code = parts[1];
            var systemParameter = options.AddParameter(VLatest.System.Value, system, includeInHash: true);
            var conditions = $"({tableName}.SystemId{suffix} = (SELECT SystemId FROM dbo.System WHERE Value = {systemParameter})";

            var hasSystem = !string.IsNullOrEmpty(system);
            var hasCode = !string.IsNullOrEmpty(code);

            if (!hasSystem)
            {
                conditions += $" OR {tableName}.SystemId{suffix} IS NULL";
            }

            conditions += ")";

            if (hasCode)
            {
                conditions += " AND ";
                conditions += BuildCodeCondition(code, options, suffix, tableName);
            }

            return conditions;
        }

        protected override string GetTableName(string modifier)
        {
            if (string.Equals(modifier, "text", StringComparison.OrdinalIgnoreCase))
            {
                return "TokenText";
            }

            return "TokenSearchParam";
        }

        private static string BuildCodeCondition(string code, ParserOptions options, string suffix, string tableName)
        {
            const int MaxCodeLength = 256;

            if (code.Length <= MaxCodeLength)
            {
                // Code fits in the Code column
                var codeParameter = options.AddParameter(VLatest.TokenSearchParam.Code, code, includeInHash: true);
                return $"{tableName}.Code{suffix} = {codeParameter}";
            }
            else
            {
                // Code is longer than 256 characters
                // The first 256 characters are in Code, the rest in CodeOverflow
                var codePrefix = code.Substring(0, MaxCodeLength);
                var codeOverflow = code.Substring(MaxCodeLength);

                var codeParameter = options.AddParameter(VLatest.TokenSearchParam.Code, codePrefix, includeInHash: true);
                var overflowParameter = options.AddParameter(VLatest.TokenSearchParam.CodeOverflow, codeOverflow, includeInHash: true);

                return $"({tableName}.Code{suffix} = {codeParameter} AND {tableName}.CodeOverflow{suffix} = {overflowParameter})";
            }
        }
    }
}
