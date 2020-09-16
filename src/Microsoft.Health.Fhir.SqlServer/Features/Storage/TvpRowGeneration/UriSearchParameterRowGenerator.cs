// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class UriSearchParameterRowGenerator : SearchParameterRowGenerator<UriSearchValue, VLatest.UriSearchParamTableTypeRow>
    {
        public UriSearchParameterRowGenerator(SqlServerFhirModel model)
            : base(model)
        {
        }

        internal override bool TryGenerateRow(short searchParamId, UriSearchValue searchValue, out VLatest.UriSearchParamTableTypeRow row)
        {
            row = new VLatest.UriSearchParamTableTypeRow(searchParamId, searchValue.Uri);
            return true;
        }
    }
}
