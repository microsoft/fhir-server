// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration.V3
{
    internal class UriSearchParameterRowGenerator : SearchParameterRowGenerator<UriSearchValue, Schema.Model.V3.UriSearchParamTableTypeRow>
    {
        public UriSearchParameterRowGenerator(SqlServerFhirModel model)
            : base(model)
        {
        }

        internal override bool TryGenerateRow(short searchParamId, UriSearchValue searchValue, out Schema.Model.V3.UriSearchParamTableTypeRow row)
        {
            row = new Schema.Model.V3.UriSearchParamTableTypeRow(searchParamId, searchValue.Uri);
            return true;
        }
    }
}
