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
    internal class TokenTokenCompositeSearchParamListRowGenerator : CompositeSearchParamRowGenerator<(TokenSearchValue component1, TokenSearchValue component2), TokenTokenCompositeSearchParamListRow>
    {
        private static readonly IEqualityComparer<TokenTokenCompositeSearchParamListRow> RowComparer = EqualityComparer<TokenTokenCompositeSearchParamListRow>.Create(
            (left, right) =>
                left.ResourceTypeId == right.ResourceTypeId &&
                left.ResourceSurrogateId == right.ResourceSurrogateId &&
                left.SearchParamId == right.SearchParamId &&
                left.SystemId1 == right.SystemId1 &&
                string.Equals(left.Code1, right.Code1, StringComparison.Ordinal) &&
                string.Equals(left.CodeOverflow1, right.CodeOverflow1, StringComparison.Ordinal) &&
                left.SystemId2 == right.SystemId2 &&
                string.Equals(left.Code2, right.Code2, StringComparison.Ordinal) &&
                string.Equals(left.CodeOverflow2, right.CodeOverflow2, StringComparison.Ordinal),
            row =>
            {
                var hash = default(HashCode);
                hash.Add(row.ResourceTypeId);
                hash.Add(row.ResourceSurrogateId);
                hash.Add(row.SearchParamId);
                hash.Add(row.SystemId1);
                hash.Add(row.Code1);
                hash.Add(row.CodeOverflow1);
                hash.Add(row.SystemId2);
                hash.Add(row.Code2);
                hash.Add(row.CodeOverflow2);
                return hash.ToHashCode();
            });

        private readonly TokenSearchParamListRowGenerator _tokenRowGenerator;

        public TokenTokenCompositeSearchParamListRowGenerator(SqlServerFhirModel model, TokenSearchParamListRowGenerator tokenRowGenerator, SearchParameterToSearchValueTypeMap searchParameterTypeMap)
            : base(model, searchParameterTypeMap, RowComparer)
        {
            _tokenRowGenerator = tokenRowGenerator;
        }

        internal override bool TryGenerateRow(short resourceTypeId, long resourceSurrogateId, short searchParamId, (TokenSearchValue component1, TokenSearchValue component2) searchValue, HashSet<TokenTokenCompositeSearchParamListRow> results, out TokenTokenCompositeSearchParamListRow row)
        {
            if (_tokenRowGenerator.TryGenerateRow(resourceTypeId, resourceSurrogateId, default, searchValue.component1, null, out var token1Row) &&
                _tokenRowGenerator.TryGenerateRow(resourceTypeId, resourceSurrogateId, default, searchValue.component2, null, out var token2Row))
            {
                row = new TokenTokenCompositeSearchParamListRow(
                    resourceTypeId,
                    resourceSurrogateId,
                    searchParamId,
                    token1Row.SystemId,
                    token1Row.Code,
                    token1Row.CodeOverflow,
                    token2Row.SystemId,
                    token2Row.Code,
                    token2Row.CodeOverflow);

                return results == null || results.Add(row);
            }

            row = default;
            return false;
        }
    }
}
