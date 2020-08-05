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
                    throw new SearchParameterNotSupportedException(string.Format(Core.Resources.SearchParameterNotSupported, sortOptions.searchParameterInfo.Name));
                }
            }

            return sortOrder;
        }

        public static (Core.Models.SearchParameterInfo, SortOrder) GetFirstSortSupportedParam(this SearchOptions searchOptions)
        {
            EnsureArg.IsNotNull(searchOptions, nameof(searchOptions));

            foreach (var sortOptions in searchOptions.Sort)
            {
                if (sortOptions.searchParameterInfo.IsSortSupported())
                {
                    return sortOptions;
                }
                else
                {
                    throw new SearchParameterNotSupportedException(string.Format(Core.Resources.SearchParameterNotSupported, sortOptions.searchParameterInfo.Name));
                }
            }

            return (null, SortOrder.Ascending);
        }
    }
}
