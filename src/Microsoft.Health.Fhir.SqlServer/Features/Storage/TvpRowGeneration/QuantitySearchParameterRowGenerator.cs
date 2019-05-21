// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class QuantitySearchParameterRowGenerator : SearchParameterRowGenerator<QuantitySearchValue, V1.QuantitySearchParamTableTypeRow>
    {
        public QuantitySearchParameterRowGenerator(SqlServerFhirModel model)
            : base(model)
        {
        }

        protected override V1.QuantitySearchParamTableTypeRow GenerateRow(short searchParamId, SearchParameterInfo searchParameter, QuantitySearchValue searchValue)
        {
            bool isSingleValue = searchValue.Low == searchValue.High;

            return new V1.QuantitySearchParamTableTypeRow(
                searchParamId,
                Model.GetSystem(searchValue.System),
                Model.GetQuantityCode(searchValue.Code),
                isSingleValue ? searchValue.Low : null,
                isSingleValue ? null : searchValue.Low ?? (decimal?)V1.QuantitySearchParam.LowValue.MinValue,
                isSingleValue ? null : searchValue.High ?? (decimal?)V1.QuantitySearchParam.HighValue.MaxValue);
        }
    }
}
