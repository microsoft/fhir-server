// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class TokenNumberNumberCompositeSearchParameterV1RowGenerator : CompositeSearchParameterRowGenerator<(TokenSearchValue component1, NumberSearchValue component2, NumberSearchValue component3), TokenNumberNumberCompositeSearchParamTableTypeV1Row>
    {
        private readonly TokenSearchParameterV1RowGenerator _tokenRowGenerator;
        private readonly NumberSearchParameterV1RowGenerator _numberV1RowGenerator;

        public TokenNumberNumberCompositeSearchParameterV1RowGenerator(SqlServerFhirModel model, TokenSearchParameterV1RowGenerator tokenRowGenerator, NumberSearchParameterV1RowGenerator numberV1RowGenerator)
            : base(model)
        {
            _tokenRowGenerator = tokenRowGenerator;
            _numberV1RowGenerator = numberV1RowGenerator;
        }

        internal override bool TryGenerateRow(short searchParamId, (TokenSearchValue component1, NumberSearchValue component2, NumberSearchValue component3) searchValue, out TokenNumberNumberCompositeSearchParamTableTypeV1Row row)
        {
            if (_tokenRowGenerator.TryGenerateRow(default, searchValue.component1, out var token1Row) &&
                _numberV1RowGenerator.TryGenerateRow(default, searchValue.component2, out var token2Row) &&
                _numberV1RowGenerator.TryGenerateRow(default, searchValue.component3, out var token3Row))
            {
                bool hasRange = token2Row.SingleValue == null || token3Row.SingleValue == null;
                row = new TokenNumberNumberCompositeSearchParamTableTypeV1Row(
                    searchParamId,
                    token1Row.SystemId,
                    token1Row.Code,
                    hasRange ? null : token2Row.SingleValue,
                    token2Row.LowValue ?? token2Row.SingleValue,
                    token2Row.HighValue ?? token2Row.SingleValue,
                    hasRange ? null : token3Row.SingleValue,
                    token3Row.LowValue ?? token3Row.SingleValue,
                    token3Row.HighValue ?? token3Row.SingleValue,
                    HasRange: hasRange);

                return true;
            }

            row = default;
            return false;
        }
    }
}
