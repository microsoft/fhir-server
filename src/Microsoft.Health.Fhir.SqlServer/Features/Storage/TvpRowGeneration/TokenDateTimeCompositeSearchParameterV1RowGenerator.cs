// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class TokenDateTimeCompositeSearchParameterV1RowGenerator : CompositeSearchParameterRowGenerator<(TokenSearchValue component1, DateTimeSearchValue component2), TokenDateTimeCompositeSearchParamTableTypeV1Row>
    {
        private readonly TokenSearchParameterV1RowGenerator _tokenRowGenerator;
        private readonly DateTimeSearchParameterV1RowGenerator _dateTimeV1RowGenerator;

        public TokenDateTimeCompositeSearchParameterV1RowGenerator(SqlServerFhirModel model, TokenSearchParameterV1RowGenerator tokenRowGenerator, DateTimeSearchParameterV1RowGenerator dateTimeV1RowGenerator)
            : base(model)
        {
            _tokenRowGenerator = tokenRowGenerator;
            _dateTimeV1RowGenerator = dateTimeV1RowGenerator;
        }

        internal override bool TryGenerateRow(short searchParamId, (TokenSearchValue component1, DateTimeSearchValue component2) searchValue, out TokenDateTimeCompositeSearchParamTableTypeV1Row row)
        {
            if (_tokenRowGenerator.TryGenerateRow(default, searchValue.component1, out var token1Row) &&
                _dateTimeV1RowGenerator.TryGenerateRow(default, searchValue.component2, out var token2Row))
            {
                row = new TokenDateTimeCompositeSearchParamTableTypeV1Row(
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
