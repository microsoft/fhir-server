// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser
{
    public class NumberSqlParser : BaseSqlParser
    {
        public NumberSqlParser(SearchParameterCollection parameterCollection)
            : base(parameterCollection)
        {
        }

        public override string BuildWhereClause(string value, string modifier, int? columnSuffix = null)
        {
            var escapedValue = ParseValue(value, out var opperator);
            var suffix = columnSuffix.HasValue ? columnSuffix.Value.ToString() : string.Empty;

            return opperator switch
            {
                ">" or ">=" => $"t.HighValue{suffix} {opperator} {escapedValue}",
                "<" or "<=" => $"t.LowValue{suffix} {opperator} {escapedValue}",
                "=" => $"t.SingleValue{suffix} = {escapedValue}",
                _ => throw new InvalidOperationException($"Unsupported operator: {opperator}"),
            };
        }

        private static string ParseValue(string value, out string opperator)
        {
            opperator = "=";

            if (string.IsNullOrEmpty(value))
            {
                return "''";
            }

            // Check for comparison prefixes
            string actualValue = value;

            if (value.StartsWith("ge", StringComparison.OrdinalIgnoreCase))
            {
                opperator = ">=";
                actualValue = value.Substring(2);
            }
            else if (value.StartsWith("gt", StringComparison.OrdinalIgnoreCase))
            {
                opperator = ">";
                actualValue = value.Substring(2);
            }
            else if (value.StartsWith("le", StringComparison.OrdinalIgnoreCase))
            {
                opperator = "<=";
                actualValue = value.Substring(2);
            }
            else if (value.StartsWith("lt", StringComparison.OrdinalIgnoreCase))
            {
                opperator = "<";
                actualValue = value.Substring(2);
            }

            // Escape single quotes by doubling them
            var escaped = actualValue.Replace("'", "''", StringComparison.Ordinal);
            return $"'{escaped}'";
        }
    }
}
