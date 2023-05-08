// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    /// <summary>
    /// Represents the search result.
    /// </summary>
    public class SearchResult
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SearchResult"/> class.
        /// </summary>
        /// <param name="results">The search results.</param>
        /// <param name="continuationToken">The continuation token.</param>
        /// <param name="sortOrder">The parameters the results are sorted by</param>
        /// <param name="unsupportedSearchParameters">The list of unsupported search parameters.</param>
        /// <param name="searchIssues">List of search issues found.</param>
        public SearchResult(
            IEnumerable<SearchResultEntry> results,
            string continuationToken,
            IReadOnlyList<(SearchParameterInfo searchParameterInfo, SortOrder sortOrder)> sortOrder,
            IReadOnlyList<Tuple<string, string>> unsupportedSearchParameters,
            IReadOnlyList<OperationOutcomeIssue> searchIssues = null)
        {
            EnsureArg.IsNotNull(results, nameof(results));
            EnsureArg.IsNotNull(unsupportedSearchParameters, nameof(unsupportedSearchParameters));

            Results = results;
            TotalCount = results.Count();
            UnsupportedSearchParameters = unsupportedSearchParameters;
            ContinuationToken = continuationToken;
            SortOrder = sortOrder;
            SearchIssues = searchIssues ?? Array.Empty<OperationOutcomeIssue>();
        }

        public SearchResult(int totalCount, IReadOnlyList<Tuple<string, string>> unsupportedSearchParameters)
        {
            EnsureArg.IsNotNull(unsupportedSearchParameters, nameof(unsupportedSearchParameters));

            UnsupportedSearchParameters = unsupportedSearchParameters;
            TotalCount = totalCount;
            Results = Enumerable.Empty<SearchResultEntry>();
            SearchIssues = Array.Empty<OperationOutcomeIssue>();
        }

        /// <summary>
        /// Gets the search results.
        /// </summary>
        public IEnumerable<SearchResultEntry> Results { get; }

        /// <summary>
        /// Gets the list of unsupported search parameters.
        /// </summary>
        public IReadOnlyList<Tuple<string, string>> UnsupportedSearchParameters { get; }

        /// <summary>
        /// Gets total number of documents.
        /// </summary>
        public int? TotalCount { get; set; }

        /// <summary>
        /// The highest resource surrogateid in the results set
        /// </summary>
        public long MaxResourceSurrogateId { get; set; }

        /// <summary>
        /// The reindex specific information
        /// </summary>
        public SearchResultReindex ReindexResult { get; set; }

        /// <summary>
        /// Gets the continuation token.
        /// </summary>
        public string ContinuationToken { get; }

        /// <summary>
        /// A list of issues that will be returned inside a search result.
        /// </summary>
        public IReadOnlyList<OperationOutcomeIssue> SearchIssues { get; }

        public IReadOnlyList<(SearchParameterInfo searchParameterInfo, SortOrder sortOrder)> SortOrder { get; }

        public static SearchResult Empty(IReadOnlyList<Tuple<string, string>> unsupportedSearchParams = null)
        {
            return new SearchResult(totalCount: 0, unsupportedSearchParams ?? new List<Tuple<string, string>>());
        }
    }
}
