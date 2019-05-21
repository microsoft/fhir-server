// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class UriSearchParameterRowGenerator : SearchParameterRowGenerator<UriSearchValue, V1.UriSearchParamTableTypeRow>
    {
        public UriSearchParameterRowGenerator(SqlServerFhirModel model)
            : base(model)
        {
        }

        protected override V1.UriSearchParamTableTypeRow GenerateRow(short searchParamId, SearchParameterInfo searchParameter, UriSearchValue searchValue)
        {
            return new V1.UriSearchParamTableTypeRow(searchParamId, searchValue.Uri);
        }
    }
}
