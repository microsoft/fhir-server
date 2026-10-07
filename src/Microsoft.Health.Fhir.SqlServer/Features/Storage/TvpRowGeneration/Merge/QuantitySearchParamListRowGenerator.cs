// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class QuantitySearchParamListRowGenerator : MergeSearchParameterRowGenerator<QuantitySearchValue, QuantitySearchParamListRow>
    {
        private static readonly IEqualityComparer<QuantitySearchParamListRow> RowComparer = EqualityComparer<QuantitySearchParamListRow>.Create(
            (left, right) =>
                left.ResourceTypeId == right.ResourceTypeId &&
                left.ResourceSurrogateId == right.ResourceSurrogateId &&
                left.SearchParamId == right.SearchParamId &&
                left.SystemId == right.SystemId &&
                left.QuantityCodeId == right.QuantityCodeId &&
                left.SingleValue == right.SingleValue &&
                left.LowValue == right.LowValue &&
                left.HighValue == right.HighValue,
            row => HashCode.Combine(row.ResourceTypeId, row.ResourceSurrogateId, row.SearchParamId, row.SystemId, row.QuantityCodeId, row.SingleValue, row.LowValue, row.HighValue));

        public QuantitySearchParamListRowGenerator(SqlServerFhirModel model, SearchParameterToSearchValueTypeMap searchParameterTypeMap)
            : base(model, searchParameterTypeMap, RowComparer)
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
