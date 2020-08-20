// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration.V4
{
    internal class QuantitySearchParameterRowGenerator : SearchParameterRowGenerator<QuantitySearchValue, Schema.Model.V4.QuantitySearchParamTableTypeRow>
    {
        public QuantitySearchParameterRowGenerator(SqlServerFhirModel model)
            : base(model)
        {
        }

        internal override bool TryGenerateRow(short searchParamId, QuantitySearchValue searchValue, out Schema.Model.V4.QuantitySearchParamTableTypeRow row)
        {
            bool isSingleValue = searchValue.Low == searchValue.High;

            row = new Schema.Model.V4.QuantitySearchParamTableTypeRow(
                searchParamId,
                string.IsNullOrWhiteSpace(searchValue.System) ? default(int?) : Model.GetSystemId(searchValue.System),
                string.IsNullOrWhiteSpace(searchValue.Code) ? default(int?) : Model.GetQuantityCodeId(searchValue.Code),
                isSingleValue ? searchValue.Low : null,
                isSingleValue ? null : searchValue.Low ?? (decimal?)Schema.Model.V4.QuantitySearchParam.LowValue.MinValue,
                isSingleValue ? null : searchValue.High ?? (decimal?)Schema.Model.V4.QuantitySearchParam.HighValue.MaxValue);

            return true;
        }
    }
}
