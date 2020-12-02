// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class TokenStringCompositeSearchParameterV1RowGenerator : CompositeSearchParameterRowGenerator<(TokenSearchValue component1, StringSearchValue component2), TokenStringCompositeSearchParamTableTypeV1Row>
    {
        private readonly TokenSearchParameterV1RowGenerator _tokenRowGenerator;
        private readonly StringSearchParameterV1RowGenerator _stringV1RowGenerator;

        public TokenStringCompositeSearchParameterV1RowGenerator(SqlServerFhirModel model, TokenSearchParameterV1RowGenerator tokenRowGenerator, StringSearchParameterV1RowGenerator stringV1RowGenerator)
            : base(model)
        {
            _tokenRowGenerator = tokenRowGenerator;
            _stringV1RowGenerator = stringV1RowGenerator;
        }

        internal override bool TryGenerateRow(short searchParamId, (TokenSearchValue component1, StringSearchValue component2) searchValue, out TokenStringCompositeSearchParamTableTypeV1Row row)
        {
            if (_tokenRowGenerator.TryGenerateRow(default, searchValue.component1, out var token1Row) &&
                _stringV1RowGenerator.TryGenerateRow(default, searchValue.component2, out var string2Row))
            {
                row = new TokenStringCompositeSearchParamTableTypeV1Row(
                    searchParamId,
                    token1Row.SystemId,
                    token1Row.Code,
                    string2Row.Text,
                    TextOverflow2: string2Row.TextOverflow);

                return true;
            }

            row = default;
            return false;
        }
    }
}
