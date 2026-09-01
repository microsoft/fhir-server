// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch
{
    /// <summary>
    /// Removes semantic results whose supporting source resources are not visible to the current request.
    /// </summary>
    public interface ISemanticSearchEvidenceFilter
    {
        /// <summary>
        /// Filters semantic results using the authorization context applied by FHIR search.
        /// </summary>
        /// <param name="searchResult">The search result containing semantic evidence.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>The filtered search result.</returns>
        Task<SearchResult> FilterAsync(SearchResult searchResult, CancellationToken cancellationToken);
    }
}
