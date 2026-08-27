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
    internal class SearchParamListRowGenerator : ITableValuedParameterRowGenerator<IReadOnlyList<ResourceSearchParameterStatus>, SearchParamListRow>
    {
        public IEnumerable<SearchParamListRow> GenerateRows(IReadOnlyList<ResourceSearchParameterStatus> searchParameterStatuses)
        {
            var currentUrls = searchParameterStatuses.Select(searchParameterStatus =>
                new SearchParamListRow(
                    searchParameterStatus.Uri.OriginalString,
                    searchParameterStatus.Status.ToString(),
                    searchParameterStatus.IsPartiallySupported,
                    searchParameterStatus.LastUpdated));
            var previousUrls = searchParameterStatuses.Where(_ => _.PreviousUri != null).Select(searchParameterStatus =>
                new SearchParamListRow(
                    searchParameterStatus.PreviousUri.OriginalString,
                    SearchParameterStatus.Deleted.ToString(),
                    searchParameterStatus.IsPartiallySupported,
                    searchParameterStatus.LastUpdated));
            return currentUrls.Concat(previousUrls);
        }
    }
}
