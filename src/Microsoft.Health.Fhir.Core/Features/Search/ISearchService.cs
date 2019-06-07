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
        /// <param name="resourceType">The resource type that should be searched. If * is specified we search all resource types.</param>
        /// <param name="queryParameters">The search queries.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A <see cref="SearchResult"/> representing the result.</returns>
        Task<SearchResult> SearchCompartmentAsync(
            string compartmentType,
            string compartmentId,
            string resourceType,
            IReadOnlyList<Tuple<string, string>> queryParameters,
            CancellationToken cancellationToken);

        Task<SearchResult> SearchHistoryAsync(
            string resourceType,
            string resourceId,
            PartialDateTime at,
            PartialDateTime since,
            PartialDateTime before,
            int? count,
            string continuationToken,
            CancellationToken cancellationToken);
    }
}
