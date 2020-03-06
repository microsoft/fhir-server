// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Search;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class SearchServiceExtensions
    {
        /// <summary>
        /// Performs a "Conditional Search", conditional searches are found in bundles and done through
        /// "If-Exists" headers on the API. Additional logic is used to filter parameters that don't restrict
        /// results, and also ensure that the query meets criteria requirements
        /// </summary>
        public static async Task<IReadOnlyCollection<SearchResultEntry>> ConditionalSearchAsync(this ISearchService searchService, string instanceType, IReadOnlyList<Tuple<string, string>> conditionalParameters, CancellationToken cancellationToken)
        {
            // Filters search parameters that can limit the number of results (e.g. _count=1)
            IReadOnlyList<Tuple<string, string>> filteredParameters = conditionalParameters
                .Where(x => !string.Equals(x.Item1, KnownQueryParameterNames.Count, StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(x.Item1, KnownQueryParameterNames.Summary, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            SearchResult results = await searchService.SearchAsync(instanceType, filteredParameters, cancellationToken);

            // Check if all parameters passed in were unused, this would result in no search parameters being applied to search results
            int? totalUnusedParameters = results?.UnsupportedSearchParameters.Count + results?.UnsupportedSortingParameters.Count;
            if (totalUnusedParameters == filteredParameters.Count)
            {
                throw new PreconditionFailedException(Core.Resources.ConditionalOperationNotSelectiveEnough);
            }

            SearchResultEntry[] matchedResults = results?.Results.Where(x => x.SearchEntryMode == ValueSets.SearchEntryMode.Match).ToArray();

            return matchedResults;
        }
    }
}
