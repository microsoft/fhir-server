// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    /// <summary>
    /// Provides functionalities to search resources.
    /// </summary>
    public interface ISearchService
    {
        /// <summary>
        /// Searches the resources using the <paramref name="queryParameters"/>.
        /// </summary>
        /// <param name="resourceType">The resource type that should be searched.</param>
        /// <param name="queryParameters">The search queries.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A <see cref="SearchResult"/> representing the result.</returns>
        Task<SearchResult> SearchAsync(
            string resourceType,
            IReadOnlyList<Tuple<string, string>> queryParameters,
            CancellationToken cancellationToken);

        /// <summary>
        /// Searches resources based on the given compartment using the <paramref name="queryParameters"/>.
        /// </summary>
        /// <param name="compartmentType">The compartment type that needs to be searched.</param>
        /// <param name="compartmentId">The compartment id along with the compartment type that needs to be seached.</param>
        /// <param name="resourceType">The resource type that should be searched. If null is specified we search all resource types.</param>
        /// <param name="queryParameters">The search queries.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="returnOriginResource">Specifies if the origin resource should also be returned.</param>
        /// <returns>A <see cref="SearchResult"/> representing the result.</returns>
        Task<SearchResult> SearchCompartmentAsync(
            string compartmentType,
            string compartmentId,
            string resourceType,
            IReadOnlyList<Tuple<string, string>> queryParameters,
            CancellationToken cancellationToken,
            bool returnOriginResource = false);

        Task<SearchResult> SearchHistoryAsync(
            string resourceType,
            string resourceId,
            PartialDateTime at,
            PartialDateTime since,
            PartialDateTime before,
            int? count,
            string continuationToken,
            CancellationToken cancellationToken);

        /// <summary>
        /// Searches resources by queryParameters and returns the raw resource,
        /// the current search param values for each resource,
        /// the history of each resource,
        /// and the total resource count of the query
        /// </summary>
        /// <param name="queryParameters">Currently composed of the _type parameter to search for set of resources</param>
        /// <param name="searchParameterHash">Value representing a current state of the search params</param>
        /// <param name="countOnly">Indicates that the query should return only count of the total resources</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>A collection of resources matching the query parameters</returns>
        Task<SearchResult> SearchForReindexAsync(
            IReadOnlyList<Tuple<string, string>> queryParameters,
            string searchParameterHash,
            bool countOnly,
            CancellationToken cancellationToken);
    }
}
