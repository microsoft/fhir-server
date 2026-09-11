// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser
{
    public class StringSqlParser : BaseSqlParser
    {
        public StringSqlParser(SqlSearchParameterDefinitionManager parameterCollection)
            : base(parameterCollection)
        {
            SetTableName("StringSearchParam");
        }

        public override string BuildWhereClause(string value, string modifier, ParserOptions options, int? columnSuffix = null, string tableName = "t")
        {
            var escapedValue = value.Replace("'", "''", StringComparison.Ordinal);
            var suffix = columnSuffix.HasValue ? columnSuffix.Value.ToString() : string.Empty;
            var isOverflow = escapedValue.Length > 256;
            var columnName = isOverflow ? "TextOverflow" : "Text";

            return modifier switch
            {
                "exact" => $"{tableName}.{columnName}{suffix} = {options.AddParameter(isOverflow ? VLatest.StringSearchParam.TextOverflow : VLatest.StringSearchParam.Text, value, includeInHash: true)} COLLATE Latin1_General_100_CS_AS",
                "contains" => $"({tableName}.{columnName}{suffix} like {options.AddParameter(isOverflow ? VLatest.StringSearchParam.TextOverflow : VLatest.StringSearchParam.Text, $"%{value}%", includeInHash: true)})",
                _ => $"({tableName}.{columnName}{suffix} like {options.AddParameter(isOverflow ? VLatest.StringSearchParam.TextOverflow : VLatest.StringSearchParam.Text, $"{value}%", includeInHash: true)})",
            };
        }
    }
}
