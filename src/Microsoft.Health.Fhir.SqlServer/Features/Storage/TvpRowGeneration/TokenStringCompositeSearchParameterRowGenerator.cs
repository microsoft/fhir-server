// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class TokenStringCompositeSearchParameterRowGenerator : CompositeSearchParameterRowGenerator<(TokenSearchValue component1, StringSearchValue component2), V1.TokenStringCompositeSearchParamTableTypeRow>
    {
        private readonly TokenSearchParameterRowGenerator _tokenRowGenerator;
        private readonly StringSearchParameterRowGenerator _stringRowGenerator;

        public TokenStringCompositeSearchParameterRowGenerator(SqlServerFhirModel model, TokenSearchParameterRowGenerator tokenRowGenerator, StringSearchParameterRowGenerator stringRowGenerator)
            : base(model)
        {
            _tokenRowGenerator = tokenRowGenerator;
            _stringRowGenerator = stringRowGenerator;
        }

        internal override bool TryGenerateRow(short searchParamId, (TokenSearchValue component1, StringSearchValue component2) searchValue, out V1.TokenStringCompositeSearchParamTableTypeRow row)
        {
            if (_tokenRowGenerator.TryGenerateRow(default, searchValue.component1, out var token1Row) &&
                _stringRowGenerator.TryGenerateRow(default, searchValue.component2, out var string2Row))
            {
                row = new V1.TokenStringCompositeSearchParamTableTypeRow(
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
