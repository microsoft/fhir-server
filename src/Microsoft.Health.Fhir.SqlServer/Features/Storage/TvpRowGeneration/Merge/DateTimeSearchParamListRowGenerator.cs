// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class DateTimeSearchParamListRowGenerator : MergeSearchParameterRowGenerator<DateTimeSearchValue, DateTimeSearchParamListRow>
    {
        private static readonly IEqualityComparer<DateTimeSearchParamListRow> RowComparer = EqualityComparer<DateTimeSearchParamListRow>.Create(
            (left, right) =>
                left.ResourceTypeId == right.ResourceTypeId &&
                left.ResourceSurrogateId == right.ResourceSurrogateId &&
                left.SearchParamId == right.SearchParamId &&
                left.StartDateTime == right.StartDateTime &&
                left.EndDateTime == right.EndDateTime &&
                left.IsLongerThanADay == right.IsLongerThanADay &&
                left.IsMin == right.IsMin &&
                left.IsMax == right.IsMax,
            row => HashCode.Combine(row.ResourceTypeId, row.ResourceSurrogateId, row.SearchParamId, row.StartDateTime, row.EndDateTime, row.IsLongerThanADay, row.IsMin, row.IsMax));

        private short _lastUpdatedSearchParamId;

        public DateTimeSearchParamListRowGenerator(SqlServerFhirModel model, SearchParameterToSearchValueTypeMap searchParameterTypeMap)
            : base(model, searchParameterTypeMap, RowComparer)
        {
        }

        internal override bool TryGenerateRow(short resourceTypeId, long resourceSurrogateId, short searchParamId, DateTimeSearchValue searchValue, HashSet<DateTimeSearchParamListRow> results, out DateTimeSearchParamListRow row)
        {
            if (searchParamId == _lastUpdatedSearchParamId)
            {
                // this value is already stored on the Resource table.
                row = default;
                return false;
            }

            row = new DateTimeSearchParamListRow(
                resourceTypeId,
                resourceSurrogateId,
                searchParamId,
                searchValue.Start,
                searchValue.End,
                Math.Abs((searchValue.End - searchValue.Start).Ticks) > TimeSpan.TicksPerDay,
                searchValue.IsMin,
                searchValue.IsMax);

            return results == null || results.Add(row);
        }

        protected override void Initialize()
        {
            _lastUpdatedSearchParamId = Model.GetSearchParamId(SearchParameterNames.LastUpdatedUri);
        }
    }
}
