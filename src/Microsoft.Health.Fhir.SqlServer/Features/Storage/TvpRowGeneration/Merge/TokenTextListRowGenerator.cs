// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class TokenTextListRowGenerator : MergeSearchParameterRowGenerator<TokenSearchValue, TokenTextListRow>
    {
        // Generated rows have no custom hashing; default struct hashing can cluster rows sharing ResourceTypeId,
        // causing quadratic HashSet equality work for large metadata sets. Hash all fields to distribute rows
        // while retaining full-field equality. Text is already normalized in the deduplication key,
        // so ordinal comparison preserves the existing semantics.
        private static readonly IEqualityComparer<TokenTextListRow> RowComparer = EqualityComparer<TokenTextListRow>.Create(
            (left, right) =>
                left.ResourceTypeId == right.ResourceTypeId &&
                left.ResourceSurrogateId == right.ResourceSurrogateId &&
                left.SearchParamId == right.SearchParamId &&
                string.Equals(left.Text, right.Text, StringComparison.Ordinal),
            row => HashCode.Combine(row.ResourceTypeId, row.ResourceSurrogateId, row.SearchParamId, row.Text));

        public TokenTextListRowGenerator(SqlServerFhirModel model, SearchParameterToSearchValueTypeMap searchParameterTypeMap)
            : base(model, searchParameterTypeMap, RowComparer)
        {
        }

        internal override bool TryGenerateRow(short resourceTypeId, long resourceSurrogateId, short searchParamId, TokenSearchValue searchValue, HashSet<TokenTextListRow> results, out TokenTextListRow row)
        {
            if (string.IsNullOrWhiteSpace(searchValue.Text))
            {
                row = default;
                return false;
            }

            row = new TokenTextListRow(resourceTypeId, resourceSurrogateId, searchParamId, searchValue.Text);
            return results == null || results.Add(new TokenTextListRow(resourceTypeId, resourceSurrogateId, searchParamId, searchValue.Text.ToLowerInvariant()));
        }
    }
}
