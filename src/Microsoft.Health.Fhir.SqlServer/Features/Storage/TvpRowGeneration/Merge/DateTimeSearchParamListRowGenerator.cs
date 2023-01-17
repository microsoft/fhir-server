// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class DateTimeSearchParamListRowGenerator : MergeSearchParameterRowGenerator<DateTimeSearchValue, DateTimeSearchParamListRow>
    {
        private short _lastUpdatedSearchParamId;

        public DateTimeSearchParamListRowGenerator(SqlServerFhirModel model, SearchParameterToSearchValueTypeMap searchParameterTypeMap)
            : base(model, searchParameterTypeMap)
        {
        }

        internal override bool TryGenerateRow(short resourceTypeId, long resourceSurrogateId, short searchParamId, DateTimeSearchValue searchValue, out DateTimeSearchParamListRow row)
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

            return true;
        }

        protected override void Initialize()
        {
            _lastUpdatedSearchParamId = Model.GetSearchParamId(SearchParameterNames.LastUpdatedUri);
        }
    }
}
