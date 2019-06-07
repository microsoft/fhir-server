// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class TokenDateTimeCompositeSearchParameterRowGenerator : CompositeSearchParameterRowGenerator<(TokenSearchValue component1, DateTimeSearchValue component2), V1.TokenDateTimeCompositeSearchParamTableTypeRow>
    {
        private readonly TokenSearchParameterRowGenerator _tokenRowGenerator;
        private readonly DateTimeSearchParameterRowGenerator _dateTimeRowGenerator;

        public TokenDateTimeCompositeSearchParameterRowGenerator(SqlServerFhirModel model, TokenSearchParameterRowGenerator tokenRowGenerator, DateTimeSearchParameterRowGenerator dateTimeRowGenerator)
            : base(model)
        {
            _tokenRowGenerator = tokenRowGenerator;
            _dateTimeRowGenerator = dateTimeRowGenerator;
        }

        internal override bool TryGenerateRow(short searchParamId, (TokenSearchValue component1, DateTimeSearchValue component2) searchValue, out V1.TokenDateTimeCompositeSearchParamTableTypeRow row)
        {
            if (_tokenRowGenerator.TryGenerateRow(default, searchValue.component1, out var token1Row) &&
                _dateTimeRowGenerator.TryGenerateRow(default, searchValue.component2, out var token2Row))
            {
                row = new V1.TokenDateTimeCompositeSearchParamTableTypeRow(
                    searchParamId,
                    token1Row.SystemId,
                    token1Row.Code,
                    token2Row.StartDateTime,
                    token2Row.EndDateTime,
                    token2Row.IsLongerThanADay);

                return true;
            }

            row = default;
            return false;
        }
    }
}
