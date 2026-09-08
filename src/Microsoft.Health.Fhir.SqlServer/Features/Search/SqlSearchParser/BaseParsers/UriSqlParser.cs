// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser
{
    /// <summary>
    /// Parser for URI search parameters (e.g., url, profile, identifier system).
    /// URI parameters support exact match and hierarchical searches using :above and :below modifiers.
    /// </summary>
    public class UriSqlParser : BaseSqlParser
    {
        public UriSqlParser(SqlSearchParameterDefinitionManager parameterCollection)
            : base(parameterCollection)
        {
            SetTableName("UriSearchParam");
        }

        public override string BuildWhereClause(string value, string modifier, ParserOptions options, int? columnSuffix = null, string tableName = "t")
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "1=1";
            }

            var suffix = columnSuffix.HasValue ? columnSuffix.Value.ToString() : string.Empty;

            if (string.IsNullOrEmpty(modifier))
            {
                // Exact match (case-sensitive)
                var uriParameter = options.AddParameter(VLatest.UriSearchParam.Uri, value, includeInHash: true);
                return $"{tableName}.Uri{suffix} = {uriParameter}";
            }

            if (string.Equals(modifier, "above", StringComparison.OrdinalIgnoreCase))
            {
                // :above modifier - matches URIs that are hierarchical ancestors
                // e.g., searching for :above http://example.com/a/b matches http://example.com/a
                // URN schemes are excluded from hierarchical matching
                var uriParameter = options.AddParameter(VLatest.UriSearchParam.Uri, value, includeInHash: true);
                return $"({uriParameter} LIKE {tableName}.Uri{suffix} + '%' AND {tableName}.Uri{suffix} NOT LIKE 'urn:%')";
            }

            if (string.Equals(modifier, "below", StringComparison.OrdinalIgnoreCase))
            {
                // :below modifier - matches URIs that are hierarchical descendants
                // e.g., searching for :below http://example.com/a matches http://example.com/a/b
                // URN schemes are excluded from hierarchical matching
                return $"({tableName}.Uri{suffix} LIKE {options.AddParameter(VLatest.UriSearchParam.Uri, $"{value}%", includeInHash: true)} AND {tableName}.Uri{suffix} NOT LIKE 'urn:%')";
            }

            // Unknown modifier - treat as exact match
            var fallbackUriParameter = options.AddParameter(VLatest.UriSearchParam.Uri, value, includeInHash: true);
            return $"{tableName}.Uri{suffix} = {fallbackUriParameter}";
        }
    }
}
