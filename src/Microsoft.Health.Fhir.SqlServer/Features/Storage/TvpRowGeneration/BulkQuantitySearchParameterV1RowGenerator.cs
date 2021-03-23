// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class BulkQuantitySearchParameterV1RowGenerator : BulkSearchParameterRowGenerator<QuantitySearchValue, BulkQuantitySearchParamTableTypeV1Row>
    {
        public BulkQuantitySearchParameterV1RowGenerator(SqlServerFhirModel model, SearchParameterToSearchValueTypeMap searchParameterTypeMap)
            : base(model, searchParameterTypeMap)
        {
        }

        internal override bool TryGenerateRow(int offset, short searchParamId, QuantitySearchValue searchValue, out BulkQuantitySearchParamTableTypeV1Row row)
        {
            bool isSingleValue = searchValue.Low == searchValue.High;

            row = new BulkQuantitySearchParamTableTypeV1Row(
                offset,
                searchParamId,
                string.IsNullOrWhiteSpace(searchValue.System) ? default(int?) : Model.GetSystemId(searchValue.System),
                string.IsNullOrWhiteSpace(searchValue.Code) ? default(int?) : Model.GetQuantityCodeId(searchValue.Code),
                isSingleValue ? searchValue.Low : null,
                isSingleValue ? null : searchValue.Low ?? (decimal?)VLatest.QuantitySearchParam.LowValue.MinValue,
                isSingleValue ? null : searchValue.High ?? (decimal?)VLatest.QuantitySearchParam.HighValue.MaxValue);

            return true;
        }
    }
}
