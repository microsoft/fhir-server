// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration.V3
{
    internal class NumberSearchParameterRowGenerator : SearchParameterRowGenerator<NumberSearchValue, Schema.Model.V3.NumberSearchParamTableTypeRow>
    {
        public NumberSearchParameterRowGenerator(SqlServerFhirModel model)
            : base(model)
        {
        }

        internal override bool TryGenerateRow(short searchParamId, NumberSearchValue searchValue, out Schema.Model.V3.NumberSearchParamTableTypeRow row)
        {
            bool isSingleValue = searchValue.Low == searchValue.High;

            row = new Schema.Model.V3.NumberSearchParamTableTypeRow(
                searchParamId,
                isSingleValue ? searchValue.Low : null,
                isSingleValue ? null : searchValue.Low ?? (decimal?)Schema.Model.V3.NumberSearchParam.LowValue.MinValue,
                isSingleValue ? null : searchValue.High ?? (decimal?)Schema.Model.V3.NumberSearchParam.HighValue.MaxValue);

            return true;
        }
    }
}
