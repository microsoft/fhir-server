// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Text;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser
{
    /// <summary>
    /// Parser for URI search parameters (e.g., url, profile, identifier system).
    /// URI parameters support exact match and hierarchical searches using :above and :below modifiers.
    /// </summary>
    public class UriSqlParser : BaseSqlParser
    {
        public UriSqlParser(SearchParameterCollection parameterCollection)
            : base(parameterCollection)
        {
        }

        protected override string BuildWhereClause(string value, string modifier)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "1=1";
            }

            var escapedUri = EscapeSqlValue(value);

            if (string.IsNullOrEmpty(modifier))
            {
                // Exact match (case-sensitive)
                return $"t.Uri = {escapedUri}";
            }

            if (modifier.Equals("above", StringComparison.OrdinalIgnoreCase))
            {
                // :above modifier - matches URIs that are hierarchical ancestors
                // e.g., searching for :above http://example.com/a/b matches http://example.com/a
                // URN schemes are excluded from hierarchical matching
                return $"({escapedUri} LIKE t.Uri + '%' AND t.Uri NOT LIKE 'urn:%')";
            }

            if (modifier.Equals("below", StringComparison.OrdinalIgnoreCase))
            {
                // :below modifier - matches URIs that are hierarchical descendants
                // e.g., searching for :below http://example.com/a matches http://example.com/a/b
                // URN schemes are excluded from hierarchical matching
                return $"(t.Uri LIKE {escapedUri} + '%' AND t.Uri NOT LIKE 'urn:%')";
            }

            // Unknown modifier - treat as exact match
            return $"t.Uri = {escapedUri}";
        }
    }
}
