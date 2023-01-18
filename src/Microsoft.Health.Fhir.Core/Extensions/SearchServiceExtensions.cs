// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
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
        private static readonly HashSet<string> _excludedParameters = new()
        {
            KnownQueryParameterNames.Count,
            KnownQueryParameterNames.Summary,
            KnownQueryParameterNames.Total,
            KnownQueryParameterNames.ContinuationToken,
            "_include",
            "_revinclude",
        };

        /// <summary>
        /// Performs a "Conditional Search", conditional searches are found in bundles and done through
        /// "If-Exists" headers on the API. Additional logic is used to filter parameters that don't restrict
        /// results, and also ensure that the query meets criteria requirements
        /// </summary>
        /// <param name="searchService">searchService</param>
        /// <param name="instanceType">The instanceType to search</param>
        /// <param name="conditionalParameters">ConditionalParameters</param>
        /// <param name="cancellationToken">a CancellationToken</param>
        /// <returns>Search collection</returns>
        public static async Task<IReadOnlyCollection<SearchResultEntry>> ConditionalSearchAsync(this ISearchService searchService, string instanceType, IReadOnlyList<Tuple<string, string>> conditionalParameters, CancellationToken cancellationToken)
        {
            // Most "Conditional" logic needs only 0, 1 or >1, so here we can limit to "2"
            (IReadOnlyCollection<SearchResultEntry> results, _) = await ConditionalSearchAsync(searchService, instanceType, conditionalParameters, 2, cancellationToken);
            return results;
        }

        /// <summary>
        /// Performs a "Conditional Search", conditional searches are found in bundles and done through
        /// "If-Exists" headers on the API. Additional logic is used to filter parameters that don't restrict
        /// results, and also ensure that the query meets criteria requirements
        /// </summary>
        /// <param name="searchService">searchService</param>
        /// <param name="instanceType">The instanceType to search</param>
        /// <param name="conditionalParameters">ConditionalParameters</param>
        /// <param name="count">the search Count</param>
        /// <param name="cancellationToken">a CancellationToken</param>
        /// <param name="continuationToken">a optional ContinuationToken</param>
        /// <returns>Search collection and a continuationToken</returns>
        /// <exception cref="PreconditionFailedException">Returns this exception when all passed in params match the search result unusedParams</exception>
        internal static async Task<(IReadOnlyCollection<SearchResultEntry> results, string continuationToken)> ConditionalSearchAsync(
            this ISearchService searchService,
            string instanceType,
            IReadOnlyList<Tuple<string, string>> conditionalParameters,
            int count,
            CancellationToken cancellationToken,
            string continuationToken = null)
        {
            // Filters search parameters that can limit the number of results (e.g. _count=1)
            IList<Tuple<string, string>> filteredParameters = conditionalParameters
                .Where(x => !_excludedParameters.Contains(x.Item1, StringComparer.OrdinalIgnoreCase))
                .ToList();

            int userProvidedParameterCount = filteredParameters.Count;

            filteredParameters.Add(Tuple.Create(KnownQueryParameterNames.Count, count.ToString(CultureInfo.InvariantCulture)));

            if (!string.IsNullOrEmpty(continuationToken))
            {
                filteredParameters.Add(Tuple.Create(KnownQueryParameterNames.ContinuationToken, ContinuationTokenConverter.Encode(continuationToken)));
            }

            SearchResult results = await searchService.SearchAsync(instanceType, filteredParameters.ToImmutableList(), cancellationToken);

            // Check if all parameters passed in were unused, this would result in no search parameters being applied to search results
            int? totalUnusedParameters = results?.UnsupportedSearchParameters.Count;
            if (totalUnusedParameters == userProvidedParameterCount)
            {
                throw new PreconditionFailedException(string.Format(CultureInfo.InvariantCulture, Core.Resources.ConditionalOperationNotSelectiveEnough, instanceType));
            }

            SearchResultEntry[] matchedResults = results?.Results.Where(x => x.SearchEntryMode == ValueSets.SearchEntryMode.Match).ToArray();

            return (matchedResults, results?.ContinuationToken);
        }
    }
}
