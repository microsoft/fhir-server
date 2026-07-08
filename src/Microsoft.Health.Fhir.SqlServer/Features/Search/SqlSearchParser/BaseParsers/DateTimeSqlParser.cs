// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser
{
    public class DateTimeSqlParser : BaseSqlParser
    {
        private readonly string _dateTimeFormat = "yyyy-MM-ddTHH:mm:ss.fffffff";

        public DateTimeSqlParser(SqlSearchParameterDefinitionManager parameterCollection)
            : base(parameterCollection)
        {
            TableName = "DateTimeSearchParam";
        }

        public override string BuildWhereClause(string value, string modifier, int? columnSuffix = null)
        {
            var parsedValue = ParseValue(value, out var valueModifier);
            var suffix = columnSuffix.HasValue ? columnSuffix.Value.ToString() : string.Empty;

            return valueModifier switch
            {
                "gt" => $"t.EndDateTime{suffix} > '{parsedValue.End.ToString(_dateTimeFormat)}'",
                "ge" => $"t.EndDateTime{suffix} >= '{parsedValue.Start.ToString(_dateTimeFormat)}'",
                "lt" => $"t.StartDateTime{suffix} < '{parsedValue.Start.ToString(_dateTimeFormat)}'",
                "le" => $"t.StartDateTime{suffix} <= '{parsedValue.End.ToString(_dateTimeFormat)}'",
                "sa" => $"t.StartDateTime{suffix} > '{parsedValue.End.ToString(_dateTimeFormat)}'",
                "eb" => $"t.EndDateTime{suffix} < '{parsedValue.Start.ToString(_dateTimeFormat)}'",
                "ne" => $"(t.EndDateTime{suffix} > '{parsedValue.End.ToString(_dateTimeFormat)}' OR t.StartDateTime{suffix} < '{parsedValue.Start.ToString(_dateTimeFormat)}')",
                "eq" => $"t.EndDateTime{suffix} >= '{parsedValue.Start.ToString(_dateTimeFormat)}' AND t.StartDateTime{suffix} <= '{parsedValue.End.ToString(_dateTimeFormat)}'",
                _ => throw new InvalidOperationException($"Unsupported modifier: {valueModifier}"),
            };
        }

        public static DateTimeSearchValue ParseValue(string value, out string modifier)
        {
            modifier = "eq";

            if (string.IsNullOrEmpty(value))
            {
                return null;
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

            // Escape single quotes by doubling them
            var parsed = DateTimeSearchValue.Parse(actualValue);

            return parsed;
        }
    }
}
