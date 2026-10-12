// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class TokenQuantityCompositeSearchParamListRowGenerator : CompositeSearchParamRowGenerator<(TokenSearchValue component1, QuantitySearchValue component2), TokenQuantityCompositeSearchParamListRow>
    {
        private static readonly IEqualityComparer<TokenQuantityCompositeSearchParamListRow> RowComparer = EqualityComparer<TokenQuantityCompositeSearchParamListRow>.Create(
            (left, right) =>
                left.ResourceTypeId == right.ResourceTypeId &&
                left.ResourceSurrogateId == right.ResourceSurrogateId &&
                left.SearchParamId == right.SearchParamId &&
                left.SystemId1 == right.SystemId1 &&
                string.Equals(left.Code1, right.Code1, StringComparison.Ordinal) &&
                string.Equals(left.CodeOverflow1, right.CodeOverflow1, StringComparison.Ordinal) &&
                left.SystemId2 == right.SystemId2 &&
                left.QuantityCodeId2 == right.QuantityCodeId2 &&
                left.SingleValue2 == right.SingleValue2 &&
                left.LowValue2 == right.LowValue2 &&
                left.HighValue2 == right.HighValue2,
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
                hash.Add(row.QuantityCodeId2);
                hash.Add(row.SingleValue2);
                hash.Add(row.LowValue2);
                hash.Add(row.HighValue2);
                return hash.ToHashCode();
            });

        private readonly TokenSearchParamListRowGenerator _tokenRowGenerator;
        private readonly QuantitySearchParamListRowGenerator _quantityRowGenerator;

        public TokenQuantityCompositeSearchParamListRowGenerator(
            SqlServerFhirModel model,
            TokenSearchParamListRowGenerator tokenRowGenerator,
            QuantitySearchParamListRowGenerator quantityV1RowGenerator,
            SearchParameterToSearchValueTypeMap searchParameterTypeMap)
            : base(model, searchParameterTypeMap, RowComparer)
        {
            _tokenRowGenerator = tokenRowGenerator;
            _quantityRowGenerator = quantityV1RowGenerator;
        }

        internal override bool TryGenerateRow(
            short resourceTypeId,
            long resourceSurrogateId,
            short searchParamId,
            (TokenSearchValue component1, QuantitySearchValue component2) searchValue,
            HashSet<TokenQuantityCompositeSearchParamListRow> results,
            out TokenQuantityCompositeSearchParamListRow row)
        {
            if (_tokenRowGenerator.TryGenerateRow(resourceTypeId, resourceSurrogateId, default, searchValue.component1, null, out var token1Row) &&
                _quantityRowGenerator.TryGenerateRow(resourceTypeId, resourceSurrogateId, default, searchValue.component2, null, out var token2Row))
            {
                row = new TokenQuantityCompositeSearchParamListRow(
                    resourceTypeId,
                    resourceSurrogateId,
                    searchParamId,
                    token1Row.SystemId,
                    token1Row.Code,
                    token1Row.CodeOverflow,
                    token2Row.SystemId,
                    token2Row.QuantityCodeId,
                    token2Row.SingleValue,
                    token2Row.LowValue,
                    token2Row.HighValue);

                return results == null || results.Add(row);
            }

            row = default;
            return false;
        }
    }
}
