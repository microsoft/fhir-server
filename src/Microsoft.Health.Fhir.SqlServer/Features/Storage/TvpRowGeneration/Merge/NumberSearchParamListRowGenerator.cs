// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class NumberSearchParamListRowGenerator : MergeSearchParameterRowGenerator<NumberSearchValue, NumberSearchParamListRow>
    {
        public NumberSearchParamListRowGenerator(SqlServerFhirModel model, SearchParameterToSearchValueTypeMap searchParameterTypeMap)
            : base(model, searchParameterTypeMap)
        {
        }

        internal override bool TryGenerateRow(short resourceTypeId, long resourceSurrogateId, short searchParamId, NumberSearchValue searchValue, HashSet<NumberSearchParamListRow> results, out NumberSearchParamListRow row)
        {
            var singleValue = searchValue.Low == searchValue.High ? searchValue.Low : null;

            row = new NumberSearchParamListRow(
                resourceTypeId,
                resourceSurrogateId,
                searchParamId,
                singleValue.HasValue ? singleValue : null,
                singleValue.HasValue ? singleValue : searchValue.Low ?? (decimal?)VLatest.NumberSearchParam.LowValue.MinValue,
                singleValue.HasValue ? singleValue : searchValue.High ?? (decimal?)VLatest.NumberSearchParam.HighValue.MaxValue);

            return results == null || results.Add(row);
        }
    }
}
