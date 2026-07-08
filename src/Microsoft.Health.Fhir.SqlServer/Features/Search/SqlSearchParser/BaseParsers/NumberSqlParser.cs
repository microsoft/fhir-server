// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Runtime.Serialization;
using System.Text;
using Microsoft.SqlServer.Management.XEvent;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser
{
    public class NumberSqlParser : BaseSqlParser
    {
        public NumberSqlParser(SqlSearchParameterDefinitionManager parameterCollection)
            : base(parameterCollection)
        {
            TableName = "NumberSearchParam";
        }

        public override string BuildWhereClause(string value, string modifier, int? columnSuffix = null)
        {
            var parsedValue = ParseValue(value, out var valueModifier);
            var suffix = columnSuffix.HasValue ? columnSuffix.Value.ToString() : string.Empty;

            return valueModifier switch
            {
                "gt" => $"t.HighValue{suffix} > {parsedValue}",
                "ge" => $"t.HighValue{suffix} >= {parsedValue}",
                "lt" => $"t.LowValue{suffix} < {parsedValue}",
                "le" => $"t.LowValue{suffix} <= {parsedValue}",
                "sa" => $"t.LowValue{suffix} > {parsedValue}",
                "eb" => $"t.HighValue{suffix} < {parsedValue}",
                "ne" => $"(t.HighValue{suffix} > {parsedValue} OR t.LowValue{suffix} < {parsedValue})",
                "eq" => $"t.HighValue{suffix} >= {parsedValue} AND t.LowValue{suffix} <= {parsedValue}",
                _ => throw new InvalidOperationException($"Unsupported modifier: {valueModifier}"),
            };
        }

        public static string ParseValue(string value, out string modifier)
        {
            modifier = "eq";

            if (string.IsNullOrEmpty(value))
            {
                return "''";
            }

            // Check for comparison prefixes
            string actualValue = value;

            if (value.StartsWith("ge", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("le", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("gt", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("lt", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("eq", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("ne", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("sa", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("eb", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("ap", StringComparison.OrdinalIgnoreCase))
            {
                modifier = value.Substring(0, 2);
                actualValue = value.Substring(2);
            }

            var parsedValue = double.TryParse(actualValue, out var numericValue) ? numericValue : throw new SerializationException($"Invalid number value: {actualValue}");
            return parsedValue.ToString();
        }
    }
}
