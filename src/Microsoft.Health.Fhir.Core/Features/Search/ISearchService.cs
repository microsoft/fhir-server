﻿// -------------------------------------------------------------------------------------------------
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
        /// <param name="isAsyncOperation">Whether the search is part of an async operation.</param>
        /// <param name="resourceVersionTypes">Which version types (latest, soft-deleted, history) to include in search.</param>
        /// <param name="onlyIds">Whether to return only the resource ids, not the full resource</param>
        /// <param name="isIncludesOperation">Whether the search is to query remaining include resources.</param>
        /// <returns>A <see cref="SearchResult"/> representing the result.</returns>
        Task<SearchResult> SearchAsync(
            string resourceType,
            IReadOnlyList<Tuple<string, string>> queryParameters,
            CancellationToken cancellationToken,
            bool isAsyncOperation = false,
            ResourceVersionType resourceVersionTypes = ResourceVersionType.Latest,
            bool onlyIds = false,
            bool isIncludesOperation = false);

        /// <summary>
        /// Searches the resources using the <paramref name="searchOptions"/>.
        /// </summary>
        /// <param name="searchOptions">The options to use during the search.</param>
        /// <param name="cancellationToken">The cancellationToken.</param>
        /// <returns>The search result.</returns>
        Task<SearchResult> SearchAsync(
            SearchOptions searchOptions,
            CancellationToken cancellationToken);

        /// <summary>
        /// Searches resources based on the given compartment using the <paramref name="queryParameters"/>.
        /// </summary>
        /// <param name="compartmentType">The compartment type that needs to be searched.</param>
        /// <param name="compartmentId">The compartment id along with the compartment type that needs to be seached.</param>
        /// <param name="resourceType">The resource type that should be searched. If null is specified we search all resource types.</param>
        /// <param name="queryParameters">The search queries.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="isAsyncOperation">Whether the search is part of an async operation.</param>
        /// <param name="useSmartCompartmentDefinition">Indicates wether to use the expanded SMART on FHIR definition of a compartment.</param>
        /// <returns>A <see cref="SearchResult"/> representing the result.</returns>
        Task<SearchResult> SearchCompartmentAsync(
            string compartmentType,
            string compartmentId,
            string resourceType,
            IReadOnlyList<Tuple<string, string>> queryParameters,
            CancellationToken cancellationToken,
            bool isAsyncOperation = false,
            bool useSmartCompartmentDefinition = false);

        Task<SearchResult> SearchHistoryAsync(
            string resourceType,
            string resourceId,
            PartialDateTime at,
            PartialDateTime since,
            PartialDateTime before,
            int? count,
            string summary,
            string continuationToken,
            string sort,
            CancellationToken cancellationToken,
            bool isAsyncOperation = false);

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
        /// <param name="isAsyncOperation">Whether the search is part of an async operation.</param>
        /// <returns>A collection of resources matching the query parameters</returns>
        Task<SearchResult> SearchForReindexAsync(
            IReadOnlyList<Tuple<string, string>> queryParameters,
            string searchParameterHash,
            bool countOnly,
            CancellationToken cancellationToken,
            bool isAsyncOperation = false);

        Task<IReadOnlyList<(long StartId, long EndId)>> GetSurrogateIdRanges(
            string resourceType,
            long startId,
            long endId,
            int rangeSize,
            int numberOfRanges,
            bool up,
            CancellationToken cancellationToken);

        Task<IReadOnlyList<string>> GetUsedResourceTypes(CancellationToken cancellationToken);

        Task<IEnumerable<string>> GetFeedRanges(CancellationToken cancellationToken);
    }
}
