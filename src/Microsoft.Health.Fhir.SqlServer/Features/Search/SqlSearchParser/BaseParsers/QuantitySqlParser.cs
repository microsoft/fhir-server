// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Text;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser.BaseParsers
{
    /// <summary>
    /// Parser for quantity search parameters (e.g., value-quantity, component-value-quantity).
    /// Quantity parameters combine numeric values with optional system and code.
    /// Format: [prefix]number|system|code or [prefix]number||code or [prefix]number
    /// Examples:
    ///   - "5.4|http://unitsofmeasure.org|mg" - value 5.4 with system and code
    ///   - "5.4||mg" - value 5.4 with code only (any system)
    ///   - "le100.0" - less than or equal to 100.0
    ///   - "gt50|http://unitsofmeasure.org|kg" - greater than 50 kg
    /// </summary>
    public class QuantitySqlParser : BaseSqlParser
    {
        public QuantitySqlParser(SqlSearchParameterDefinitionManager parameterCollection)
            : base(parameterCollection)
        {
            TableName = "QuantitySearchParam";
        }

        public override string BuildWhereClause(string value, string modifier, int? columnSuffix = null, string tableName = "t")
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "1=1";
            }

            // Parse quantity value - format can be:
            // - "[prefix]number" (just value)
            // - "[prefix]number|system|code" (value with system and code)
            // - "[prefix]number||code" (value with code, any system)
            // - "[prefix]number|system|" (value with system, any code)

            var parts = value.Split('|', 3);
            var numberPart = parts[0];
            string? system = parts.Length > 1 ? parts[1] : null;
            string? code = parts.Length > 2 ? parts[2] : null;

            var suffix = columnSuffix.HasValue ? columnSuffix.Value.ToString() : string.Empty;
            var conditions = new StringBuilder();

            // Parse and add the numeric comparison
            var numericCondition = BuildNumericCondition(numberPart, suffix, tableName);
            conditions.Append(numericCondition);

            // Add system condition if specified
            if (!string.IsNullOrEmpty(system))
            {
                var escapedSystem = EscapeSqlValue(system);
                conditions.Append($" AND {tableName}.SystemId{suffix} = (SELECT SystemId FROM dbo.System WHERE Value = {escapedSystem})");
            }

            // Add code condition if specified
            if (!string.IsNullOrEmpty(code))
            {
                var escapedCode = EscapeSqlValue(code);
                conditions.Append($" AND {tableName}.QuantityCodeId{suffix} = (SELECT QuantityCodeId FROM dbo.QuantityCode WHERE Value = {escapedCode})");
            }

            return conditions.ToString();
        }

        /// <summary>
        /// Builds the numeric portion of the WHERE clause using the value parser from NumberSqlParser.
        /// </summary>
        /// <param name="numberPart">The numeric part of the quantity value (may include prefix like "gt", "le", etc.).</param>
        /// <param name="suffix">Optional numeric suffix for column names in composite tables.</param>
        /// <param name="tableName">The name of the table to use in the SQL condition.</param>
        /// <returns>The SQL condition for the numeric comparison.</returns>
        private static string BuildNumericCondition(string numberPart, string suffix, string tableName)
        {
            // Reuse the NumberSqlParser's ParseValue to extract the modifier and value
            var parsedValue = NumberSqlParser.ParseValue(numberPart, out var valueModifier);

            // Build the condition using the same logic as NumberSqlParser
            return valueModifier switch
            {
                "gt" => $"{tableName}.HighValue{suffix} > {parsedValue}",
                "ge" => $"{tableName}.HighValue{suffix} >= {parsedValue}",
                "lt" => $"{tableName}.LowValue{suffix} < {parsedValue}",
                "le" => $"{tableName}.LowValue{suffix} <= {parsedValue}",
                "sa" => $"{tableName}.LowValue{suffix} > {parsedValue}",       // starts after
                "eb" => $"{tableName}.HighValue{suffix} < {parsedValue}",      // ends before
                "ne" => $"({tableName}.HighValue{suffix} > {parsedValue} OR {tableName}.LowValue{suffix} < {parsedValue})",
                "eq" => $"{tableName}.HighValue{suffix} >= {parsedValue} AND {tableName}.LowValue{suffix} <= {parsedValue}",
                "ap" => BuildApproximateCondition(parsedValue, suffix, tableName), // approximately
                _ => throw new InvalidOperationException($"Unsupported modifier: {valueModifier}"),
            };
        }

        /// <summary>
        /// Builds the condition for approximate matching (ap modifier).
        /// Approximate means within 10% of the specified value.
        /// </summary>
        /// <param name="escapedValue">The escaped numeric value.</param>
        /// <param name="suffix">Optional numeric suffix for column names.</param>
        /// <param name="tableName">The name of the table to use in the SQL condition.</param>
        /// <returns>The SQL condition for approximate matching.</returns>
        private static string BuildApproximateCondition(string escapedValue, string suffix, string tableName)
        {
            // Remove the quotes added by ParseValue to do math
            var numericValue = escapedValue.Trim('\'');

            // For approximate, we check if the ranges overlap when considering 10% tolerance
            // The stored range is [LowValue, HighValue]
            // The approximate range is [value * 0.9, value * 1.1]
            // They overlap if: HighValue >= value * 0.9 AND LowValue <= value * 1.1
            return $"({tableName}.HighValue{suffix} >= {numericValue} * 0.9 AND {tableName}.LowValue{suffix} <= {numericValue} * 1.1)";
        }
    }
}
