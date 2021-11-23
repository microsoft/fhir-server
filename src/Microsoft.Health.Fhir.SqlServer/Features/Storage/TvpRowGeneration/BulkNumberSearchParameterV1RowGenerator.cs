﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class BulkNumberSearchParameterV1RowGenerator : BulkSearchParameterRowGenerator<NumberSearchValue, BulkNumberSearchParamTableTypeV1Row>
    {
        public BulkNumberSearchParameterV1RowGenerator(SqlServerFhirModel model, SearchParameterToSearchValueTypeMap searchParameterTypeMap)
            : base(model, searchParameterTypeMap)
        {
        }

        internal override bool TryGenerateRow(int offset, short searchParamId, NumberSearchValue searchValue, out BulkNumberSearchParamTableTypeV1Row row)
        {
            var singleValue = searchValue.Low == searchValue.High ? searchValue.Low : null;

            row = new BulkNumberSearchParamTableTypeV1Row(
                offset,
                searchParamId,
                singleValue.HasValue ? singleValue : null,
                singleValue.HasValue ? singleValue : searchValue.Low ?? (decimal?)VLatest.NumberSearchParam.LowValue.MinValue,
                singleValue.HasValue ? singleValue : searchValue.High ?? (decimal?)VLatest.NumberSearchParam.HighValue.MaxValue);

            return true;
        }
    }
}
