// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class TokenQuantityCompositeSearchParameterRowGenerator : CompositeSearchParameterRowGenerator<(TokenSearchValue component1, QuantitySearchValue component2), V1.TokenQuantityCompositeSearchParamTableTypeRow>
    {
        private readonly TokenSearchParameterRowGenerator _tokenRowGenerator;
        private readonly QuantitySearchParameterRowGenerator _quantityRowGenerator;

        public TokenQuantityCompositeSearchParameterRowGenerator(SqlServerFhirModel model, TokenSearchParameterRowGenerator tokenRowGenerator, QuantitySearchParameterRowGenerator quantityRowGenerator)
            : base(model)
        {
            _tokenRowGenerator = tokenRowGenerator;
            _quantityRowGenerator = quantityRowGenerator;
        }

        internal override bool TryGenerateRow(short searchParamId, (TokenSearchValue component1, QuantitySearchValue component2) searchValue, out V1.TokenQuantityCompositeSearchParamTableTypeRow row)
        {
            if (_tokenRowGenerator.TryGenerateRow(default, searchValue.component1, out var token1Row) &&
                _quantityRowGenerator.TryGenerateRow(default, searchValue.component2, out var token2Row))
            {
                row = new V1.TokenQuantityCompositeSearchParamTableTypeRow(
                    searchParamId,
                    token1Row.SystemId,
                    token1Row.Code,
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
