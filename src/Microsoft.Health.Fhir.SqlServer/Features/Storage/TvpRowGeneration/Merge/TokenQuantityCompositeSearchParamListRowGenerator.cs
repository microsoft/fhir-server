// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class TokenQuantityCompositeSearchParamListRowGenerator : CompositeSearchParamRowGenerator<(TokenSearchValue component1, QuantitySearchValue component2), TokenQuantityCompositeSearchParamListRow>
    {
        private readonly TokenSearchParamListRowGenerator _tokenRowGenerator;
        private readonly QuantitySearchParamListRowGenerator _quantityRowGenerator;

        public TokenQuantityCompositeSearchParamListRowGenerator(
            SqlServerFhirModel model,
            TokenSearchParamListRowGenerator tokenRowGenerator,
            QuantitySearchParamListRowGenerator quantityV1RowGenerator,
            SearchParameterToSearchValueTypeMap searchParameterTypeMap)
            : base(model, searchParameterTypeMap)
        {
            _tokenRowGenerator = tokenRowGenerator;
            _quantityRowGenerator = quantityV1RowGenerator;
        }

        internal override bool TryGenerateRow(
            short resourceTypeId,
            long resourceSurrogateId,
            short searchParamId,
            (TokenSearchValue component1, QuantitySearchValue component2) searchValue,
            out TokenQuantityCompositeSearchParamListRow row)
        {
            if (_tokenRowGenerator.TryGenerateRow(resourceTypeId, resourceSurrogateId, default, searchValue.component1, out var token1Row) &&
                _quantityRowGenerator.TryGenerateRow(resourceTypeId, resourceSurrogateId, default, searchValue.component2, out var token2Row))
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

                return true;
            }

            row = default;
            return false;
        }
    }
}
