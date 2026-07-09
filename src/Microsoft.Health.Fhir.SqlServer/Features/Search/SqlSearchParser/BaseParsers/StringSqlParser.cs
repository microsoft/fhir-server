// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Text;
using Microsoft.Health.Extensions.DependencyInjection;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser
{
    public class StringSqlParser : BaseSqlParser
    {
        public StringSqlParser(SqlSearchParameterDefinitionManager parameterCollection)
            : base(parameterCollection)
        {
            SetTableName("StringSearchParam");
        }

        public override string BuildWhereClause(string value, string modifier, int? columnSuffix = null, string tableName = "t")
        {
            var escapedValue = value.Replace("'", "''", StringComparison.Ordinal);
            var suffix = columnSuffix.HasValue ? columnSuffix.Value.ToString() : string.Empty;

            return modifier switch
            {
                "exact" => $"{tableName}.Text{(escapedValue.Length > 256 ? "Overflow" : string.Empty)}{suffix} = '{escapedValue}' COLLATE Latin1_General_Bin",
                "contains" => $"({tableName}.Text{suffix} like '%{escapedValue}%' OR {tableName}.TextOverflow{suffix} like '%{escapedValue}%')",
                _ => $"({tableName}.Text{suffix} like '%{escapedValue}%' OR {tableName}.TextOverflow{suffix} like '%{escapedValue}%')",
            };
        }
    }
}
