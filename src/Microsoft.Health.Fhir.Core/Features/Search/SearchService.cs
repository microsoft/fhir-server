// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Core;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    /// <summary>
    /// Provides the base implementation of the <see cref="ISearchService"/>.
    /// </summary>
    public abstract class SearchService : ISearchService
    {
        private readonly ISearchOptionsFactory _searchOptionsFactory;
        private readonly IFhirDataStore _fhirDataStore;

        /// <summary>
        /// Initializes a new instance of the <see cref="SearchService"/> class.
        /// </summary>
        /// <param name="searchOptionsFactory">The search options factory.</param>
        /// <param name="fhirDataStore">The data store</param>
        protected SearchService(ISearchOptionsFactory searchOptionsFactory, IFhirDataStore fhirDataStore)
        {
            EnsureArg.IsNotNull(searchOptionsFactory, nameof(searchOptionsFactory));
            EnsureArg.IsNotNull(fhirDataStore, nameof(fhirDataStore));

            _searchOptionsFactory = searchOptionsFactory;
            _fhirDataStore = fhirDataStore;
        }

        /// <inheritdoc />
        public async Task<SearchResult> SearchAsync(
            string resourceType,
            IReadOnlyList<Tuple<string, string>> queryParameters,
            CancellationToken cancellationToken,
            bool isAsyncOperation = false,
            ResourceVersionType resourceVersionTypes = ResourceVersionType.Latest)
        {
            SearchOptions searchOptions = _searchOptionsFactory.Create(resourceType, queryParameters, isAsyncOperation, resourceVersionTypes);

            // Execute the actual search.
            return await SearchAsync(searchOptions, cancellationToken);
        }

        /// <inheritdoc />
        public async Task<SearchResult> SearchCompartmentAsync(
            string compartmentType,
            string compartmentId,
            string resourceType,
            IReadOnlyList<Tuple<string, string>> queryParameters,
            CancellationToken cancellationToken,
            bool isAsyncOperation = false,
            bool useSmartCompartmentDefinition = false)
        {
            SearchOptions searchOptions = _searchOptionsFactory.Create(compartmentType, compartmentId, resourceType, queryParameters, isAsyncOperation, useSmartCompartmentDefinition);

            // Execute the actual search.
            return await SearchAsync(searchOptions, cancellationToken);
        }

        public async Task<SearchResult> SearchHistoryAsync(
            string resourceType,
            string resourceId,
            PartialDateTime at,
            PartialDateTime since,
            PartialDateTime before,
            int? count,
            string continuationToken,
            string sort,
            CancellationToken cancellationToken,
            bool isAsyncOperation = false)
        {
            var queryParameters = new List<Tuple<string, string>>();

            if (at != null)
            {
                if (since != null)
                {
                    // _at and _since cannot be both specified.
                    throw new InvalidSearchOperationException(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            Core.Resources.AtCannotBeSpecifiedWithBeforeOrSince,
                            KnownQueryParameterNames.At,
                            KnownQueryParameterNames.Since));
                }

                if (before != null)
                {
                    // _at and _since cannot be both specified.
                    throw new InvalidSearchOperationException(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            Core.Resources.AtCannotBeSpecifiedWithBeforeOrSince,
                            KnownQueryParameterNames.At,
                            KnownQueryParameterNames.Before));
                }
            }

            if (before != null)
            {
                var beforeOffset = before.ToDateTimeOffset().ToUniversalTime();

                if (beforeOffset.CompareTo(Clock.UtcNow) > 0)
                {
                    // you cannot specify a value for _before in the future
                    throw new InvalidSearchOperationException(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            Core.Resources.HistoryParameterBeforeCannotBeFuture,
                            KnownQueryParameterNames.Before));
                }
            }

            bool searchByResourceId = !string.IsNullOrEmpty(resourceId);

            if (searchByResourceId)
            {
                queryParameters.Add(Tuple.Create(SearchParameterNames.Id, resourceId));
            }

            if (!string.IsNullOrEmpty(continuationToken))
            {
                queryParameters.Add(Tuple.Create(KnownQueryParameterNames.ContinuationToken, continuationToken));
            }

            if (at != null)
            {
                queryParameters.Add(Tuple.Create(KnownQueryParameterNames.At, at.ToString()));
            }
            else
            {
                if (since != null)
                {
                    queryParameters.Add(Tuple.Create(SearchParameterNames.LastUpdated, $"ge{since}"));
                }

                if (before != null)
                {
                    queryParameters.Add(Tuple.Create(SearchParameterNames.LastUpdated, $"lt{before}"));
                }
            }

            if (count.HasValue && count > 0)
            {
                queryParameters.Add(Tuple.Create(KnownQueryParameterNames.Count, count.ToString()));
            }

            if (!string.IsNullOrEmpty(sort))
            {
                if (!string.Equals(sort.TrimStart('-'), KnownQueryParameterNames.LastUpdated, StringComparison.Ordinal))
                {
                    throw new InvalidSearchOperationException(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            Core.Resources.SearchSortParameterNotSupported,
                            sort));
                }

                queryParameters.Add(Tuple.Create(KnownQueryParameterNames.Sort, sort));
            }
            else
            {
                queryParameters.Add(Tuple.Create(KnownQueryParameterNames.Sort, $"-{KnownQueryParameterNames.LastUpdated}"));
            }

            SearchOptions searchOptions = _searchOptionsFactory.Create(resourceType, queryParameters, isAsyncOperation, ResourceVersionType.Histoy);

            SearchResult searchResult = await SearchAsync(searchOptions, cancellationToken);

            // If no results are returned from the _history search
            // determine if the resource actually exists or if the results were just filtered out.
            // The 'deleted' state has no effect because history will return deleted resources
            if (searchByResourceId && searchResult.Results.Any() == false)
            {
                var resource = await _fhirDataStore.GetAsync(new ResourceKey(resourceType, resourceId), cancellationToken);

                if (resource == null)
                {
                    throw new ResourceNotFoundException(string.Format(Core.Resources.ResourceNotFoundById, resourceType, resourceId));
                }
            }

            return searchResult;
        }

        public async Task<SearchResult> SearchForReindexAsync(
            IReadOnlyList<Tuple<string, string>> queryParameters,
            string searchParameterHash,
            bool countOnly,
            CancellationToken cancellationToken,
            bool isAsyncOperation = false)
        {
            SearchOptions searchOptions = _searchOptionsFactory.Create(null, queryParameters, isAsyncOperation);

            if (countOnly)
            {
                searchOptions.CountOnly = true;
            }

            var results = await SearchForReindexInternalAsync(searchOptions, searchParameterHash, cancellationToken);

            return results;
        }

        public virtual Task<IReadOnlyList<(long StartId, long EndId)>> GetSurrogateIdRanges(
            string resourceType,
            long startId,
            long endId,
            int rangeSize,
            int numberOfRanges,
            bool up,
            CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public abstract Task<IReadOnlyList<string>> GetUsedResourceTypes(CancellationToken cancellationToken);

        /// <inheritdoc />
        public abstract Task<SearchResult> SearchAsync(
            SearchOptions searchOptions,
            CancellationToken cancellationToken);

        protected abstract Task<SearchResult> SearchForReindexInternalAsync(
            SearchOptions searchOptions,
            string searchParameterHash,
            CancellationToken cancellationToken);
     }
}
