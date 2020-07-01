// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Schema.Model;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class SearchParameterRegistryRowGenerator : ITableValuedParameterRowGenerator<List<ResourceSearchParameterStatus>, VLatest.SearchParamRegistryTableTypeRow>
    {
        public SearchParameterRegistryRowGenerator()
        {
        }

        public IEnumerable<VLatest.SearchParamRegistryTableTypeRow> GenerateRows(List<ResourceSearchParameterStatus> searchParameterStatus)
        {
            return searchParameterStatus.Select(status => new VLatest.SearchParamRegistryTableTypeRow(status.Uri.ToString(), status.Status.ToString(), status.IsPartiallySupported)).ToList();
        }
    }
}
