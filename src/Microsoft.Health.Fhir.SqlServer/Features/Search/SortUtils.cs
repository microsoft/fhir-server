// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    public static class SortUtils
    {
        public static SortOrder GetSortOrderByParameterName(SearchOptions searchOptions, string parameter)
        {
            var sortOrder = SortOrder.Ascending;
            if (searchOptions.Sort?.Count > 0)
            {
                sortOrder = searchOptions
                    .Sort
                    .Where(x => string.Equals(x.searchParameterInfo.Name, parameter, StringComparison.OrdinalIgnoreCase))
                    .Select(s => s.sortOrder).First();
            }

            return sortOrder;
        }
    }
}
