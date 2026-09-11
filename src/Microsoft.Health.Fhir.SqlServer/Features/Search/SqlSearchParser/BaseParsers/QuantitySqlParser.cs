// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Text;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

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
            SetTableName("QuantitySearchParam");
        }

        public override string BuildWhereClause(string value, string modifier, ParserOptions options, int? columnSuffix = null, string tableName = "t")
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
            var numericCondition = BuildNumericCondition(numberPart, options, suffix, tableName);
            conditions.Append(numericCondition);

            // Add system condition if specified
            if (!string.IsNullOrEmpty(system))
            {
                var systemParameter = options.AddParameter(VLatest.System.Value, system, includeInHash: true);
                conditions.Append($" AND {tableName}.SystemId{suffix} = (SELECT SystemId FROM dbo.System WHERE Value = {systemParameter})");
            }

            // Add code condition if specified
            if (!string.IsNullOrEmpty(code))
            {
                var codeParameter = options.AddParameter(VLatest.QuantityCode.Value, code, includeInHash: true);
                conditions.Append($" AND {tableName}.QuantityCodeId{suffix} = (SELECT QuantityCodeId FROM dbo.QuantityCode WHERE Value = {codeParameter})");
            }

            return conditions.ToString();
        }

        /// <summary>
        /// Builds the numeric portion of the WHERE clause using the value parser from NumberSqlParser.
        /// </summary>
        /// <param name="numberPart">The numeric part of the quantity value (may include prefix like "gt", "le", etc.).</param>
        /// <param name="options">The request-scoped parser options used to bind SQL parameters.</param>
        /// <param name="suffix">Optional numeric suffix for column names in composite tables.</param>
        /// <param name="tableName">The name of the table to use in the SQL condition.</param>
        /// <returns>The SQL condition for the numeric comparison.</returns>
        private static string BuildNumericCondition(string numberPart, ParserOptions options, string suffix, string tableName)
        {
            // Reuse the NumberSqlParser's ParseValue to extract the modifier and value
            var parsedValue = NumberSqlParser.ParseValue(numberPart, out var valueModifier);

            return NumberSqlParser.BuildNumericCondition(
                parsedValue,
                valueModifier,
                options,
                suffix,
                tableName,
                VLatest.QuantitySearchParam.HighValue,
                "HighValue",
                VLatest.QuantitySearchParam.LowValue,
                "LowValue");
        }
    }
}
