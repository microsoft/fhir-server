// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration.V3
{
    internal class QuantitySearchParameterRowGenerator : SearchParameterRowGenerator<QuantitySearchValue, Schema.Model.V3.QuantitySearchParamTableTypeRow>
    {
        public QuantitySearchParameterRowGenerator(SqlServerFhirModel model)
            : base(model)
        {
        }

        internal override bool TryGenerateRow(short searchParamId, QuantitySearchValue searchValue, out Schema.Model.V3.QuantitySearchParamTableTypeRow row)
        {
            bool isSingleValue = searchValue.Low == searchValue.High;

            row = new Schema.Model.V3.QuantitySearchParamTableTypeRow(
                searchParamId,
                string.IsNullOrWhiteSpace(searchValue.System) ? default(int?) : Model.GetSystemId(searchValue.System),
                string.IsNullOrWhiteSpace(searchValue.Code) ? default(int?) : Model.GetQuantityCodeId(searchValue.Code),
                isSingleValue ? searchValue.Low : null,
                isSingleValue ? null : searchValue.Low ?? (decimal?)Schema.Model.V3.QuantitySearchParam.LowValue.MinValue,
                isSingleValue ? null : searchValue.High ?? (decimal?)Schema.Model.V3.QuantitySearchParam.HighValue.MaxValue);

            return true;
        }
    }
}
