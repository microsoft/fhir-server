﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class BulkTokenQuantityCompositeSearchParameterV2RowGenerator : BulkCompositeSearchParameterRowGenerator<(TokenSearchValue component1, QuantitySearchValue component2),
        BulkTokenQuantityCompositeSearchParamTableTypeV2Row>
    {
        private readonly BulkTokenSearchParameterV2RowGenerator _tokenRowGenerator;
        private readonly BulkQuantitySearchParameterV2RowGenerator _quantityV2RowGenerator;

        public BulkTokenQuantityCompositeSearchParameterV2RowGenerator(
            SqlServerFhirModel model,
            BulkTokenSearchParameterV2RowGenerator tokenRowGenerator,
            BulkQuantitySearchParameterV2RowGenerator quantityV2RowGenerator,
            SearchParameterToSearchValueTypeMap searchParameterTypeMap)
            : base(model, searchParameterTypeMap)
        {
            _tokenRowGenerator = tokenRowGenerator;
            _quantityV2RowGenerator = quantityV2RowGenerator;
        }

        internal override bool TryGenerateRow(
            int offset,
            short searchParamId,
            (TokenSearchValue component1, QuantitySearchValue component2) searchValue,
            out BulkTokenQuantityCompositeSearchParamTableTypeV2Row row)
        {
            if (_tokenRowGenerator.TryGenerateRow(default, default, searchValue.component1, out var token1Row) &&
                _quantityV2RowGenerator.TryGenerateRow(default, default, searchValue.component2, out var token2Row))
            {
                row = new BulkTokenQuantityCompositeSearchParamTableTypeV2Row(
                    offset,
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
