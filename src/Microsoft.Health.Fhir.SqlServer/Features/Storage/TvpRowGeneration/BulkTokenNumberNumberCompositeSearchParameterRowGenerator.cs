// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class BulkTokenNumberNumberCompositeSearchParameterRowGenerator : BulkCompositeSearchParameterRowGenerator<(TokenSearchValue component1, NumberSearchValue component2, NumberSearchValue component3), VLatest.BulkTokenNumberNumberCompositeSearchParamTableTypeRow>
    {
        private readonly BulkTokenSearchParameterRowGenerator _tokenRowGenerator;
        private readonly BulkNumberSearchParameterRowGenerator _numberRowGenerator;

        public BulkTokenNumberNumberCompositeSearchParameterRowGenerator(SqlServerFhirModel model, BulkTokenSearchParameterRowGenerator tokenRowGenerator, BulkNumberSearchParameterRowGenerator numberRowGenerator)
            : base(model)
        {
            _tokenRowGenerator = tokenRowGenerator;
            _numberRowGenerator = numberRowGenerator;
        }

        internal override bool TryGenerateRow(int id, short searchParamId, (TokenSearchValue component1, NumberSearchValue component2, NumberSearchValue component3) searchValue, out VLatest.BulkTokenNumberNumberCompositeSearchParamTableTypeRow row)
        {
            if (_tokenRowGenerator.TryGenerateRow(default, default, searchValue.component1, out var token1Row) &&
                _numberRowGenerator.TryGenerateRow(default, default, searchValue.component2, out var token2Row) &&
                _numberRowGenerator.TryGenerateRow(default, default, searchValue.component3, out var token3Row))
            {
                bool hasRange = token2Row.SingleValue == null || token3Row.SingleValue == null;
                row = new VLatest.BulkTokenNumberNumberCompositeSearchParamTableTypeRow(
                    id,
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
