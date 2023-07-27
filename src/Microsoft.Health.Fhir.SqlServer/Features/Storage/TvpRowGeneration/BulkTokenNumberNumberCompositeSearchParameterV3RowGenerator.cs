// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class BulkTokenNumberNumberCompositeSearchParameterV3RowGenerator : BulkCompositeSearchParameterRowGenerator<(TokenSearchValue component1, NumberSearchValue component2, NumberSearchValue component3), BulkTokenNumberNumberCompositeSearchParamTableTypeV3Row>
    {
        private readonly BulkTokenSearchParameterV2RowGenerator _tokenRowGenerator;
        private readonly BulkNumberSearchParameterV2RowGenerator _numberV2RowGenerator;

        public BulkTokenNumberNumberCompositeSearchParameterV3RowGenerator(SqlServerFhirModel model, BulkTokenSearchParameterV2RowGenerator tokenRowGenerator, BulkNumberSearchParameterV2RowGenerator numberV2RowGenerator, SearchParameterToSearchValueTypeMap searchParameterTypeMap)
            : base(model, searchParameterTypeMap)
        {
            _tokenRowGenerator = tokenRowGenerator;
            _numberV2RowGenerator = numberV2RowGenerator;
        }

        internal override bool TryGenerateRow(
            int offset,
            short searchParamId,
            (TokenSearchValue component1, NumberSearchValue component2, NumberSearchValue component3) searchValue,
            out BulkTokenNumberNumberCompositeSearchParamTableTypeV3Row row)
        {
            if (_tokenRowGenerator.TryGenerateRow(default, default, searchValue.component1, out var token1Row) &&
                _numberV2RowGenerator.TryGenerateRow(default, default, searchValue.component2, out var token2Row) &&
                _numberV2RowGenerator.TryGenerateRow(default, default, searchValue.component3, out var token3Row))
            {
                bool hasRange = token2Row.SingleValue == null || token3Row.SingleValue == null;
                row = new BulkTokenNumberNumberCompositeSearchParamTableTypeV3Row(
                    offset,
                    searchParamId,
                    token1Row.SystemId,
                    token1Row.Code,
                    token1Row.CodeOverflow,
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
