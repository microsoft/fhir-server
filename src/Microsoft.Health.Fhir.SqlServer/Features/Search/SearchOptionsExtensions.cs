// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    public static class SearchOptionsExtensions
    {
        // Returns the sort order of first supported _sort query parameter

        public static (Core.Models.SearchParameterInfo, SortOrder) GetFirstSupportedSortParam(this SearchOptions searchOptions)
        {
            EnsureArg.IsNotNull(searchOptions, nameof(searchOptions));
            var sortParams = searchOptions.Sort.Where(x => x.searchParameterInfo.IsSortSupported());
            if (sortParams.Count() > 1)
            {
                // We don't support more than one sort param.
                throw new SearchParameterNotSupportedException(Core.Resources.MultiSortParameterNotSupported);
            }

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

        /// <summary>
        /// A helper method to determain if a supported sort param exists.
        /// </summary>
        /// <param name="searchOptions">The SearchOptions context</param>
        /// <returns>True if exists, false otherwise.</returns>
        public static bool SupportedSortParamExists(this SearchOptions searchOptions)
        {
            Core.Models.SearchParameterInfo searchParamInfo = GetFirstSupportedSortParam(searchOptions).Item1;

            // any sort param is valid, except _ldateUpdated because it's a special case
            return searchParamInfo != null && searchParamInfo.Name != Core.Features.KnownQueryParameterNames.LastUpdated;
        }
    }
}
