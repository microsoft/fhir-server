// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search.Filters;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    public sealed class SearchResultFilter : ISearchResultFilter
    {
        private readonly IReadOnlyList<IFilterCriteria> _filterCriterias;

        public SearchResultFilter(MissingDataFilterCriteria missingDataFilterCriteria)
        {
            EnsureArg.IsNotNull(missingDataFilterCriteria);

            // Set of filter criteria to be applied on top of a SearchResult.
            _filterCriterias = new IFilterCriteria[]
            {
                missingDataFilterCriteria,
            };
        }

        public SearchResult Filter(SearchResult searchResult)
        {
            EnsureArg.IsNotNull(searchResult);

            foreach (IFilterCriteria filterCriteria in _filterCriterias)
            {
                searchResult = filterCriteria.Apply(searchResult);
            }

            return searchResult;
        }
    }
}
