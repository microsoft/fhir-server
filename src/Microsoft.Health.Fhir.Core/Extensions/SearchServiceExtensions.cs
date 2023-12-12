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
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Logging;

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
        /// <param name="cancellationToken">The CancellationToken</param>
        /// <param name="count">The search Count.</param>
        /// <param name="continuationToken">An optional ContinuationToken</param>
        /// <param name="logger">A logger instance</param>
        /// <returns>Search collection and a continuationToken</returns>
        /// <exception cref="PreconditionFailedException">Returns this exception when all passed in params match the search result unusedParams</exception>
        internal static async Task<(IReadOnlyCollection<SearchResultEntry> Results, string ContinuationToken)> ConditionalSearchAsync(
            this ISearchService searchService,
            string instanceType,
            IReadOnlyList<Tuple<string, string>> conditionalParameters,
            CancellationToken cancellationToken,
            int? count = 2, // Most "Conditional" logic needs only 0, 1 or >1, so here we can limit to "2"
            string continuationToken = null,
            ILogger logger = null)
        {
            // Filters search parameters that can limit the number of results (e.g. _count=1)
            IList<Tuple<string, string>> filteredParameters = conditionalParameters
                .Where(x => !_excludedParameters.Contains(x.Item1, StringComparer.OrdinalIgnoreCase))
                .ToList();

            int userProvidedParameterCount = filteredParameters.Count;

            if (count != null)
            {
                filteredParameters.Add(Tuple.Create(KnownQueryParameterNames.Count, count.ToString()));
            }

            var matchedResults = new List<SearchResultEntry>();
            string lastContinuationToken = continuationToken;
            LongRunningOperationStatistics statistics = new LongRunningOperationStatistics(operationName: "conditionalSearchAsync");
            try
            {
                statistics.StartCollectingResults();
                do
                {
                    var searchParameters = new List<Tuple<string, string>>(filteredParameters);
                    if (!string.IsNullOrEmpty(lastContinuationToken))
                    {
                        searchParameters.Add(Tuple.Create(KnownQueryParameterNames.ContinuationToken, ContinuationTokenConverter.Encode(lastContinuationToken)));
                    }

                    statistics.Iterate();

                    SearchResult results = await searchService.SearchAsync(instanceType, searchParameters.ToImmutableList(), cancellationToken);
                    lastContinuationToken = results?.ContinuationToken;

                    // Check if all parameters passed in were unused, this would result in no search parameters being applied to search results
                    int? totalUnusedParameters = results?.UnsupportedSearchParameters.Count;
                    if (totalUnusedParameters == userProvidedParameterCount)
                    {
                        logger?.LogInformation("PreconditionFailed: ConditionalOperationNotSelectiveEnough");
                        throw new PreconditionFailedException(string.Format(CultureInfo.InvariantCulture, Core.Resources.ConditionalOperationNotSelectiveEnough, instanceType));
                    }

                    if (results?.Results.Any() == true)
                    {
                        matchedResults.AddRange(
                            results?.Results
                                .Where(x => x.SearchEntryMode == ValueSets.SearchEntryMode.Match)
                                .Take(Math.Max(count.HasValue ? 0 : results.Results.Count(), count.GetValueOrDefault() - matchedResults.Count)));
                    }
                }
                while (count.HasValue && matchedResults.Count < count && !string.IsNullOrEmpty(lastContinuationToken));
            }
            finally
            {
                statistics.StopCollectingResults();

                if (statistics.IterationCount > 1 && logger != null)
                {
                    try
                    {
                        logger.LogInformation(statistics.GetStatisticsAsJson());
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Error computing operation statistics. This error will not block the operation processing.");
                    }
                }
            }

            return (matchedResults, lastContinuationToken);
        }
    }
}
