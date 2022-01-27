// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Search;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class BulkQuantitySearchParameterV2RowGenerator : BulkSearchParameterRowGenerator<QuantitySearchValue, BulkQuantitySearchParamTableTypeV2Row>
    {
        public BulkQuantitySearchParameterV2RowGenerator(SqlServerFhirModel model, SearchParameterToSearchValueTypeMap searchParameterTypeMap)
            : base(model, searchParameterTypeMap)
        {
        }

        internal override bool TryGenerateRow(int offset, short searchParamId, QuantitySearchValue searchValue, out BulkQuantitySearchParamTableTypeV2Row row)
        {
            var singleValue = searchValue.Low == searchValue.High ? searchValue.Low : null;

            row = new BulkQuantitySearchParamTableTypeV2Row(
                offset,
                searchParamId,
                string.IsNullOrWhiteSpace(searchValue.System) ? SqlSearchConstants.NullId : Model.GetSystemId(searchValue.System),
                string.IsNullOrWhiteSpace(searchValue.Code) ? SqlSearchConstants.NullId : Model.GetQuantityCodeId(searchValue.Code),
                singleValue.HasValue ? singleValue : null,
                (decimal)(singleValue.HasValue ? singleValue : searchValue.Low ?? (decimal?)VLatest.QuantitySearchParam.LowValue.MinValue),
                (decimal)(singleValue.HasValue ? singleValue : searchValue.High ?? (decimal?)VLatest.QuantitySearchParam.HighValue.MaxValue));

            return true;
        }
    }
}
