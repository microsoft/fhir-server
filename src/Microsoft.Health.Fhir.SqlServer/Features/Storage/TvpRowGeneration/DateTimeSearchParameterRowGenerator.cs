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
    internal class DateTimeSearchParameterRowGenerator : SearchParameterRowGenerator<DateTimeSearchValue, V1.DateTimeSearchParamTableTypeRow>
    {
        private short _lastUpdatedSearchParamId;

        public DateTimeSearchParameterRowGenerator(SqlServerFhirModel model)
            : base(model)
        {
        }

        internal override bool TryGenerateRow(short searchParamId, DateTimeSearchValue searchValue, out V1.DateTimeSearchParamTableTypeRow row)
        {
            if (searchParamId == _lastUpdatedSearchParamId)
            {
                // this value is already stored on the Resource table.
                row = default;
                return false;
            }

            row = new V1.DateTimeSearchParamTableTypeRow(
                searchParamId,
                searchValue.Start,
                searchValue.End,
                (searchValue.Start - searchValue.End).Ticks > TimeSpan.TicksPerDay);

            return true;
        }

        protected override void Initialize()
        {
            _lastUpdatedSearchParamId = Model.GetSearchParamId(SearchParameterNames.LastUpdatedUri);
        }
    }
}
