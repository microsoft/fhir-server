// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search.Filters;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    public sealed class SearchResultFilter : ISearchResultFilter
    {
        private readonly bool _isUSCoreEnabled;

        public SearchResultFilter(bool isUSCoreEnabled)
        {
            _isUSCoreEnabled = isUSCoreEnabled;
        }

        public static SearchResultFilter Default => new SearchResultFilter(isUSCoreEnabled: false);

        public SearchResult Filter(bool isSmartRequest, SearchResult searchResult)
        {
            EnsureArg.IsNotNull(searchResult);

            // Set of filter criteria to be applied on top of a SearchResult.
            IFilterCriteria[] filterCriterias =
            {
                new MissingDataFilterCriteria(isCriteriaEnabled: _isUSCoreEnabled, isSmartRequest: isSmartRequest),
            };

            foreach (IFilterCriteria filterCriteria in filterCriterias)
            {
                searchResult = filterCriteria.Apply(searchResult);
            }

            return searchResult;
        }
    }
}
