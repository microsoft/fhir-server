﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class BulkDateTimeSearchParameterV2RowGenerator : BulkSearchParameterRowGenerator<DateTimeSearchValue, BulkDateTimeSearchParamTableTypeV2Row>
    {
        private short _lastUpdatedSearchParamId;

        public BulkDateTimeSearchParameterV2RowGenerator(SqlServerFhirModel model, SearchParameterToSearchValueTypeMap searchParameterTypeMap)
            : base(model, searchParameterTypeMap)
        {
        }

        internal override bool TryGenerateRow(int offset, short searchParamId, DateTimeSearchValue searchValue, out BulkDateTimeSearchParamTableTypeV2Row row)
        {
            if (searchParamId == _lastUpdatedSearchParamId)
            {
                // this value is already stored on the Resource table.
                row = default;
                return false;
            }

            row = new BulkDateTimeSearchParamTableTypeV2Row(
                offset,
                searchParamId,
                searchValue.Start,
                searchValue.End,
                (searchValue.Start - searchValue.End).Ticks > TimeSpan.TicksPerDay,
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
