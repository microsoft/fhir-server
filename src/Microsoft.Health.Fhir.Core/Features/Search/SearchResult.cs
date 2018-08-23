// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;

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
        public SearchResult(IEnumerable<ResourceWrapper> results, string continuationToken)
        {
            EnsureArg.IsNotNull(results, nameof(results));

            Results = results;
            ContinuationToken = continuationToken;
        }

        /// <summary>
        /// Gets the search results.
        /// </summary>
        public IEnumerable<ResourceWrapper> Results { get; }

        /// <summary>
        /// Gets total number of documents
        /// </summary>
        public int? TotalCount { get; internal set; }

        /// <summary>
        /// Gets the continuation token.
        /// </summary>
        public string ContinuationToken { get; }
    }
}
