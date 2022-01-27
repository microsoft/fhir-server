﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Search;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class BulkReferenceSearchParameterV2RowGenerator : BulkSearchParameterRowGenerator<ReferenceSearchValue, BulkReferenceSearchParamTableTypeV2Row>
    {
        public BulkReferenceSearchParameterV2RowGenerator(SqlServerFhirModel model, SearchParameterToSearchValueTypeMap searchParameterTypeMap)
            : base(model, searchParameterTypeMap)
        {
        }

        internal override bool TryGenerateRow(int offset, short searchParamId, ReferenceSearchValue searchValue, out BulkReferenceSearchParamTableTypeV2Row row)
        {
            row = new BulkReferenceSearchParamTableTypeV2Row(
                offset,
                searchParamId,
                searchValue.BaseUri == null ? string.Empty : searchValue.BaseUri.ToString(),
                string.IsNullOrWhiteSpace(searchValue.ResourceType) ? SqlSearchConstants.NullId : Model.GetResourceTypeId(searchValue.ResourceType),
                searchValue.ResourceId,
                ReferenceResourceVersion: null);

            return true;
        }
    }
}
