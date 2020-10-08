// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class BulkUriSearchParameterRowGenerator : BulkSearchParameterRowGenerator<UriSearchValue, VLatest.BulkUriSearchParamTableTypeRow>
    {
        public BulkUriSearchParameterRowGenerator(SqlServerFhirModel model)
            : base(model)
        {
        }

        internal override bool TryGenerateRow(int id, short searchParamId, UriSearchValue searchValue, out VLatest.BulkUriSearchParamTableTypeRow row)
        {
            row = new VLatest.BulkUriSearchParamTableTypeRow(id, searchParamId, searchValue.Uri);
            return true;
        }
    }
}
