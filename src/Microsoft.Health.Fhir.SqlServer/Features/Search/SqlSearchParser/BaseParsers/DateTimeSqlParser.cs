// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser
{
    public class DateTimeSqlParser : BaseSqlParser
    {
        public DateTimeSqlParser(SqlSearchParameterDefinitionManager parameterCollection)
            : base(parameterCollection)
        {
            SetTableName("DateTimeSearchParam");
        }

        public override string BuildWhereClause(string value, string modifier, ParserOptions options, int? columnSuffix = null, string tableName = "t")
        {
            var parsedValue = ParseValue(value, out var valueModifier);
            var suffix = columnSuffix.HasValue ? columnSuffix.Value.ToString() : string.Empty;
            string endDateTimeReference = $"{tableName}.EndDateTime{suffix}";
            string startDateTimeReference = $"{tableName}.StartDateTime{suffix}";

            return valueModifier switch
            {
                "gt" => $"{endDateTimeReference} > {options.AddParameter(VLatest.DateTimeSearchParam.EndDateTime, parsedValue.End, includeInHash: true)}",
                "ge" => $"{endDateTimeReference} >= {options.AddParameter(VLatest.DateTimeSearchParam.EndDateTime, parsedValue.Start, includeInHash: true)}",
                "lt" => $"{startDateTimeReference} < {options.AddParameter(VLatest.DateTimeSearchParam.StartDateTime, parsedValue.Start, includeInHash: true)}",
                "le" => $"{startDateTimeReference} <= {options.AddParameter(VLatest.DateTimeSearchParam.StartDateTime, parsedValue.End, includeInHash: true)}",
                "sa" => $"{startDateTimeReference} > {options.AddParameter(VLatest.DateTimeSearchParam.StartDateTime, parsedValue.End, includeInHash: true)}",
                "eb" => $"{endDateTimeReference} < {options.AddParameter(VLatest.DateTimeSearchParam.EndDateTime, parsedValue.Start, includeInHash: true)}",
                "ne" => $"({endDateTimeReference} > {options.AddParameter(VLatest.DateTimeSearchParam.EndDateTime, parsedValue.End, includeInHash: true)} OR {startDateTimeReference} < {options.AddParameter(VLatest.DateTimeSearchParam.StartDateTime, parsedValue.Start, includeInHash: true)})",
                "eq" => $"{endDateTimeReference} >= {options.AddParameter(VLatest.DateTimeSearchParam.EndDateTime, parsedValue.Start, includeInHash: true)} AND {startDateTimeReference} <= {options.AddParameter(VLatest.DateTimeSearchParam.StartDateTime, parsedValue.End, includeInHash: true)}",
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

            string actualValue = NumberSqlParser.ExtractModifierValue(value, out modifier);
            return DateTimeSearchValue.Parse(actualValue);
        }
    }
}
