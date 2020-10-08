// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class BulkNumberSearchParameterRowGenerator : BulkSearchParameterRowGenerator<NumberSearchValue, VLatest.BulkNumberSearchParamTableTypeRow>
    {
        public BulkNumberSearchParameterRowGenerator(SqlServerFhirModel model)
            : base(model)
        {
        }

        internal override bool TryGenerateRow(int id, short searchParamId, NumberSearchValue searchValue, out VLatest.BulkNumberSearchParamTableTypeRow row)
        {
            bool isSingleValue = searchValue.Low == searchValue.High;

            row = new VLatest.BulkNumberSearchParamTableTypeRow(
                id,
                searchParamId,
                isSingleValue ? searchValue.Low : null,
                isSingleValue ? null : searchValue.Low ?? (decimal?)VLatest.NumberSearchParam.LowValue.MinValue,
                isSingleValue ? null : searchValue.High ?? (decimal?)VLatest.NumberSearchParam.HighValue.MaxValue);

            return true;
        }
    }
}
