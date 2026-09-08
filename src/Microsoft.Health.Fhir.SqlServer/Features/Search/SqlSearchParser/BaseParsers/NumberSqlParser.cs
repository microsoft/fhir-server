// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Globalization;
using System.Runtime.Serialization;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser
{
    public class NumberSqlParser : BaseSqlParser
    {
        private const int ModifierLength = 2;

        private static readonly string[] SupportedModifiers = new[]
        {
            "ge",
            "le",
            "gt",
            "lt",
            "eq",
            "ne",
            "sa",
            "eb",
            "ap",
        };

        public NumberSqlParser(SqlSearchParameterDefinitionManager parameterCollection)
            : base(parameterCollection)
        {
            SetTableName("NumberSearchParam");
        }

        public override string BuildWhereClause(string value, string modifier, ParserOptions options, int? columnSuffix = null, string tableName = "t")
        {
            var parsedValue = ParseValue(value, out var valueModifier);
            var suffix = columnSuffix.HasValue ? columnSuffix.Value.ToString() : string.Empty;
            return BuildNumericCondition(
                parsedValue,
                valueModifier,
                options,
                suffix,
                tableName,
                VLatest.NumberSearchParam.HighValue,
                "HighValue",
                VLatest.NumberSearchParam.LowValue,
                "LowValue");
        }

        public static decimal ParseValue(string value, out string modifier)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new SerializationException("Invalid number value: ");
            }

            string actualValue = ExtractModifierValue(value, out modifier);

            return decimal.TryParse(actualValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var numericValue)
                ? numericValue
                : throw new SerializationException($"Invalid number value: {actualValue}");
        }

        internal static string ExtractModifierValue(string value, out string modifier)
        {
            modifier = "eq";

            if (value.Length < ModifierLength)
            {
                return value;
            }

            string prefix = value.Substring(0, ModifierLength);
            if (!Array.Exists(SupportedModifiers, supportedModifier => prefix.Equals(supportedModifier, StringComparison.OrdinalIgnoreCase)))
            {
                return value;
            }

            modifier = prefix;
            return value.Substring(ModifierLength);
        }

        internal static string BuildNumericCondition(
            decimal parsedValue,
            string valueModifier,
            ParserOptions options,
            string suffix,
            string tableName,
            Column highValueColumn,
            string highValueColumnName,
            Column lowValueColumn,
            string lowValueColumnName)
        {
            string highValueReference = $"{tableName}.{highValueColumnName}{suffix}";
            string lowValueReference = $"{tableName}.{lowValueColumnName}{suffix}";

            return valueModifier switch
            {
                "gt" => $"{highValueReference} > {options.AddParameter(highValueColumn, parsedValue, includeInHash: true)}",
                "ge" => $"{highValueReference} >= {options.AddParameter(highValueColumn, parsedValue, includeInHash: true)}",
                "lt" => $"{lowValueReference} < {options.AddParameter(lowValueColumn, parsedValue, includeInHash: true)}",
                "le" => $"{lowValueReference} <= {options.AddParameter(lowValueColumn, parsedValue, includeInHash: true)}",
                "sa" => $"{lowValueReference} > {options.AddParameter(lowValueColumn, parsedValue, includeInHash: true)}",
                "eb" => $"{highValueReference} < {options.AddParameter(highValueColumn, parsedValue, includeInHash: true)}",
                "ne" => $"({highValueReference} > {options.AddParameter(highValueColumn, parsedValue, includeInHash: true)} OR {lowValueReference} < {options.AddParameter(parsedValue, includeInHash: true)})",
                "eq" => $"{highValueReference} >= {options.AddParameter(highValueColumn, parsedValue, includeInHash: true)} AND {lowValueReference} <= {options.AddParameter(parsedValue, includeInHash: true)}",
                "ap" => BuildApproximateCondition(parsedValue, options, suffix, tableName, highValueColumn, highValueColumnName, lowValueColumn, lowValueColumnName),
                _ => throw new InvalidOperationException($"Unsupported modifier: {valueModifier}"),
            };
        }

        private static string BuildApproximateCondition(
            decimal parsedValue,
            ParserOptions options,
            string suffix,
            string tableName,
            Column highValueColumn,
            string highValueColumnName,
            Column lowValueColumn,
            string lowValueColumnName)
        {
            string highValueReference = $"{tableName}.{highValueColumnName}{suffix}";
            string lowValueReference = $"{tableName}.{lowValueColumnName}{suffix}";
            var lowerBoundParameter = options.AddParameter(highValueColumn, parsedValue, includeInHash: true);
            var upperBoundParameter = options.AddParameter(parsedValue, includeInHash: true);

            return $"({highValueReference} >= {lowerBoundParameter} * 0.9 AND {lowValueReference} <= {upperBoundParameter} * 1.1)";
        }
    }
}
