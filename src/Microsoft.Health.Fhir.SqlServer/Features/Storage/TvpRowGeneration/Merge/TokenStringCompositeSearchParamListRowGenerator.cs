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
    internal class TokenStringCompositeSearchParamListRowGenerator : CompositeSearchParamRowGenerator<(TokenSearchValue component1, StringSearchValue component2), TokenStringCompositeSearchParamListRow>
    {
        private static readonly IEqualityComparer<TokenStringCompositeSearchParamListRow> RowComparer = EqualityComparer<TokenStringCompositeSearchParamListRow>.Create(
            (left, right) =>
                left.ResourceTypeId == right.ResourceTypeId &&
                left.ResourceSurrogateId == right.ResourceSurrogateId &&
                left.SearchParamId == right.SearchParamId &&
                left.SystemId1 == right.SystemId1 &&
                string.Equals(left.Code1, right.Code1, StringComparison.Ordinal) &&
                string.Equals(left.CodeOverflow1, right.CodeOverflow1, StringComparison.Ordinal) &&
                string.Equals(left.Text2, right.Text2, StringComparison.Ordinal) &&
                string.Equals(left.TextOverflow2, right.TextOverflow2, StringComparison.Ordinal),
            row => HashCode.Combine(row.ResourceTypeId, row.ResourceSurrogateId, row.SearchParamId, row.SystemId1, row.Code1, row.CodeOverflow1, row.Text2, row.TextOverflow2));

        private readonly TokenSearchParamListRowGenerator _tokenRowGenerator;
        private readonly StringSearchParamListRowGenerator _stringRowGenerator;

        public TokenStringCompositeSearchParamListRowGenerator(
            SqlServerFhirModel model,
            TokenSearchParamListRowGenerator tokenRowGenerator,
            StringSearchParamListRowGenerator stringRowGenerator,
            SearchParameterToSearchValueTypeMap searchParameterTypeMap)
            : base(model, searchParameterTypeMap, RowComparer)
        {
            _tokenRowGenerator = tokenRowGenerator;
            _stringRowGenerator = stringRowGenerator;
        }

        internal override bool TryGenerateRow(
            short resourceTypeId,
            long resourceSurrogateId,
            short searchParamId,
            (TokenSearchValue component1, StringSearchValue component2) searchValue,
            HashSet<TokenStringCompositeSearchParamListRow> results,
            out TokenStringCompositeSearchParamListRow row)
        {
            if (_tokenRowGenerator.TryGenerateRow(resourceTypeId, resourceSurrogateId, default, searchValue.component1, null, out var token1Row) &&
                _stringRowGenerator.TryGenerateRow(resourceTypeId, resourceSurrogateId, default, searchValue.component2, null, out var string2Row))
            {
                row = new TokenStringCompositeSearchParamListRow(
                            resourceTypeId,
                            resourceSurrogateId,
                            searchParamId,
                            token1Row.SystemId,
                            token1Row.Code,
                            CodeOverflow1: token1Row.CodeOverflow,
                            string2Row.Text,
                            TextOverflow2: string2Row.TextOverflow);

                return results == null || results.Add(new TokenStringCompositeSearchParamListRow(
                                                            resourceTypeId,
                                                            resourceSurrogateId,
                                                            searchParamId,
                                                            token1Row.SystemId,
                                                            token1Row.Code,
                                                            CodeOverflow1: token1Row.CodeOverflow,
                                                            string2Row.Text?.ToLowerInvariant(),
                                                            TextOverflow2: string2Row.TextOverflow?.ToLowerInvariant()));
            }

            row = default;
            return false;
        }
    }
}
