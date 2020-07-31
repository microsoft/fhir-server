// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    public static class SearchOptionsExtensions
    {
        // Returns the sort order of first supported _sort query parameter

        public static SortOrder GetFirstSortOrderForSupportedParam(this SearchOptions searchOptions)
        {
            EnsureArg.IsNotNull(searchOptions, nameof(searchOptions));

            var sortOrder = SortOrder.Ascending;

            foreach (var sortOptions in searchOptions.Sort)
            {
                if (sortOptions.searchParameterInfo.IsSortSupported())
                {
                    return sortOptions.sortOrder;
                }
                else
                {
                    throw new SearchParameterNotSupportedException(string.Format(Core.Resources.SearchSortParameterNotSupported, sortOptions.searchParameterInfo.Name));
                }
            }

            return sortOrder;
        }
    }
}
