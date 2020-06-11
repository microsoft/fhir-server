// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Search;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    public static class SortUtils
    {
        private static readonly IList<string> SupportedParameters = new List<string>
            {
                KnownQueryParameterNames.LastUpdated,
            };

        public static SortOrder GetSortOrderForSupportedParam(SearchOptions searchOptions)
        {
            var sortOrder = SortOrder.Ascending;

            if (searchOptions.Sort?.Count > 0)
            {
                sortOrder = searchOptions
                    .Sort
                    .Where(x => IsSearchParamterSupported(x.searchParameterInfo.Name))
                    .Select(s => s.sortOrder).FirstOrDefault();
            }

            return sortOrder;
        }

        public static bool IsSearchParamterSupported(string parameter)
        {
            return SupportedParameters.Contains(parameter);
        }
    }
}
