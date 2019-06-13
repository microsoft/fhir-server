// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class QuantitySearchParameterRowGenerator : SearchParameterRowGenerator<QuantitySearchValue, V1.QuantitySearchParamTableTypeRow>
    {
        public QuantitySearchParameterRowGenerator(SqlServerFhirModel model)
            : base(model)
        {
        }

        internal override bool TryGenerateRow(short searchParamId, QuantitySearchValue searchValue, out V1.QuantitySearchParamTableTypeRow row)
        {
            bool isSingleValue = searchValue.Low == searchValue.High;

            row = new V1.QuantitySearchParamTableTypeRow(
                searchParamId,
                string.IsNullOrWhiteSpace(searchValue.System) ? default(int?) : Model.GetSystemId(searchValue.System),
                string.IsNullOrWhiteSpace(searchValue.Code) ? default(int?) : Model.GetQuantityCodeId(searchValue.Code),
                isSingleValue ? searchValue.Low : null,
                isSingleValue ? null : searchValue.Low ?? (decimal?)V1.QuantitySearchParam.LowValue.MinValue,
                isSingleValue ? null : searchValue.High ?? (decimal?)V1.QuantitySearchParam.HighValue.MaxValue);

            return true;
        }
    }
}
