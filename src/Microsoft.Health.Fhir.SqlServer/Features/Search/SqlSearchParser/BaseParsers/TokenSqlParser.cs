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
        public TokenSqlParser(SearchParameterCollection parameterCollection)
            : base(parameterCollection)
        {
        }

        protected override string BuildWhereClause(string value, string modifier)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "1=1";
            }

            // Parse token value - format can be:
            // - "code" (just code)
            // - "|code" (any system with this code)
            // - "system|code" (specific system and code)
            // - "system|" (any code in this system)

            var parts = value.Split('|', 2);

            if (parts.Length == 1)
            {
                // Just code, no system specified
                return BuildCodeCondition(parts[0], modifier);
            }

            var system = parts[0];
            var code = parts[1];

            var conditions = new StringBuilder();

            if (!string.IsNullOrEmpty(system))
            {
                // System is specified
                var escapedSystem = EscapeSqlValue(system);
                conditions.Append($"t.SystemId = (SELECT SystemId FROM dbo.System WHERE Value = {escapedSystem})");
            }

            if (!string.IsNullOrEmpty(code))
            {
                // Code is specified
                if (conditions.Length > 0)
                {
                    conditions.Append(" AND ");
                }

                conditions.Append(BuildCodeCondition(code, modifier));
            }
            else if (conditions.Length == 0)
            {
                // Both system and code are empty ("|")
                return "1=1";
            }

            return conditions.ToString();
        }

        private static string BuildCodeCondition(string code, string modifier)
        {
            const int MaxCodeLength = 256;

            if (code.Length <= MaxCodeLength)
            {
                // Code fits in the Code column
                var escapedCode = EscapeSqlValue(code);
                return $"t.Code = {escapedCode}";
            }
            else
            {
                // Code is longer than 256 characters
                // The first 256 characters are in Code, the rest in CodeOverflow
                var codePrefix = code.Substring(0, MaxCodeLength);
                var codeOverflow = code.Substring(MaxCodeLength);

                var escapedPrefix = EscapeSqlValue(codePrefix);
                var escapedOverflow = EscapeSqlValue(codeOverflow);

                return $"(t.Code = {escapedPrefix} AND t.CodeOverflow = {escapedOverflow})";
            }
        }
    }
}
