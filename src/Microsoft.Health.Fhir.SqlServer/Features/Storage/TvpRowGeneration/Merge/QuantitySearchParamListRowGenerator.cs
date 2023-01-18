// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class QuantitySearchParamListRowGenerator : MergeSearchParameterRowGenerator<QuantitySearchValue, QuantitySearchParamListRow>
    {
        public QuantitySearchParamListRowGenerator(SqlServerFhirModel model, SearchParameterToSearchValueTypeMap searchParameterTypeMap)
            : base(model, searchParameterTypeMap)
        {
        }

        internal override bool TryGenerateRow(short resourceTypeId, long resourceSurrogateId, short searchParamId, QuantitySearchValue searchValue, HashSet<QuantitySearchParamListRow> results, out QuantitySearchParamListRow row)
        {
            var singleValue = searchValue.Low == searchValue.High ? searchValue.Low : null;

            row = new QuantitySearchParamListRow(
                resourceTypeId,
                resourceSurrogateId,
                searchParamId,
                string.IsNullOrWhiteSpace(searchValue.System) ? default(int?) : Model.GetSystemId(searchValue.System),
                string.IsNullOrWhiteSpace(searchValue.Code) ? default(int?) : Model.GetQuantityCodeId(searchValue.Code),
                singleValue.HasValue ? singleValue : null,
                singleValue.HasValue ? singleValue : searchValue.Low ?? (decimal?)VLatest.QuantitySearchParam.LowValue.MinValue,
                singleValue.HasValue ? singleValue : searchValue.High ?? (decimal?)VLatest.QuantitySearchParam.HighValue.MaxValue);

            return results == null || results.Add(row);
        }
    }
}
