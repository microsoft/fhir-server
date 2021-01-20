// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class UriSearchParameterV2RowGenerator : SearchParameterRowGenerator<UriSearchValue, UriSearchParamTableTypeV2Row>
    {
        public UriSearchParameterV2RowGenerator(SqlServerFhirModel model)
            : base(model)
        {
        }

        internal override bool TryGenerateRow(short searchParamId, UriSearchValue searchValue, out UriSearchParamTableTypeV2Row row)
        {
            UriSearchValue canonical = searchValue.IsCanonical ? searchValue : null;
            row = new UriSearchParamTableTypeV2Row(searchParamId, searchValue.Uri, canonical?.Version, canonical?.Fragment);
            return true;
        }
    }
}
