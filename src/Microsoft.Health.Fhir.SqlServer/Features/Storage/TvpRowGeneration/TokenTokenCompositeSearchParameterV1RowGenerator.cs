// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class TokenTokenCompositeSearchParameterV1RowGenerator : CompositeSearchParameterRowGenerator<(TokenSearchValue component1, TokenSearchValue component2), TokenTokenCompositeSearchParamTableTypeV1Row>
    {
        private readonly TokenSearchParameterV1RowGenerator _tokenRowGenerator;

        public TokenTokenCompositeSearchParameterV1RowGenerator(SqlServerFhirModel model, TokenSearchParameterV1RowGenerator tokenRowGenerator)
            : base(model)
        {
            _tokenRowGenerator = tokenRowGenerator;
        }

        internal override bool TryGenerateRow(short searchParamId, (TokenSearchValue component1, TokenSearchValue component2) searchValue, out TokenTokenCompositeSearchParamTableTypeV1Row row)
        {
            if (_tokenRowGenerator.TryGenerateRow(default, searchValue.component1, out var token1Row) &&
                _tokenRowGenerator.TryGenerateRow(default, searchValue.component2, out var token2Row))
            {
                row = new TokenTokenCompositeSearchParamTableTypeV1Row(
                    searchParamId,
                    token1Row.SystemId,
                    token1Row.Code,
                    token2Row.SystemId,
                    token2Row.Code);

                return true;
            }

            row = default;
            return false;
        }
    }
}
