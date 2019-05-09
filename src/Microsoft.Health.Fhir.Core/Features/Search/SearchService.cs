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
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    /// <summary>
    /// Provides the base implementation of the <see cref="ISearchService"/>.
    /// </summary>
    public abstract class SearchService : ISearchService, IProvideCapability
    {
        private readonly ISearchOptionsFactory _searchOptionsFactory;
        private readonly IBundleFactory _bundleFactory;
        private readonly IFhirDataStore _fhirDataStore;

        // Value which is subtracted from Now when querying _history without _before specified
        private readonly int historyCurrentTimeBufferInSeconds = -3;

        /// <summary>
        /// Initializes a new instance of the <see cref="SearchService"/> class.
        /// </summary>
        /// <param name="searchOptionsFactory">The search options factory.</param>
        /// <param name="bundleFactory">The bundle factory</param>
        /// <param name="fhirDataStore">The data store</param>
        protected SearchService(ISearchOptionsFactory searchOptionsFactory, IBundleFactory bundleFactory, IFhirDataStore fhirDataStore)
        {
            EnsureArg.IsNotNull(searchOptionsFactory, nameof(searchOptionsFactory));

            _searchOptionsFactory = searchOptionsFactory;
            _bundleFactory = bundleFactory;
            _fhirDataStore = fhirDataStore;
        }

        /// <inheritdoc />
        public async Task<Bundle> SearchAsync(
            string resourceType,
            IReadOnlyList<Tuple<string, string>> queryParameters,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            SearchOptions searchOptions = _searchOptionsFactory.Create(resourceType, queryParameters);

            // Execute the actual search.
            SearchResult result = await SearchInternalAsync(searchOptions, cancellationToken);

            return _bundleFactory.CreateSearchBundle(searchOptions.UnsupportedSearchParams, result);
        }

        /// <inheritdoc />
        public async Task<Bundle> SearchCompartmentAsync(string compartmentType, string compartmentId, string resourceType, IReadOnlyList<Tuple<string, string>> queryParameters, CancellationToken cancellationToken)
        {
            SearchOptions searchOptions = _searchOptionsFactory.Create(compartmentType, compartmentId, resourceType, queryParameters);

            // Execute the actual search.
            SearchResult result = await SearchInternalAsync(searchOptions, cancellationToken);

            return _bundleFactory.CreateSearchBundle(searchOptions.UnsupportedSearchParams, result);
        }

        public async Task<Bundle> SearchHistoryAsync(
            string resourceType,
            string resourceId,
            PartialDateTime at,
            PartialDateTime since,
            PartialDateTime before,
            int? count,
            string continuationToken,
            CancellationToken cancellationToken)
        {
            var queryParameters = new List<Tuple<string, string>>();
            DateTimeOffset? addedBefore = null;

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
                var beforeOffset = before.ToDateTimeOffset(
                    defaultMonth: 1,
                    defaultDaySelector: (year, month) => 1,
                    defaultHour: 0,
                    defaultMinute: 0,
                    defaultSecond: 0,
                    defaultFraction: 0.0000000m,
                    defaultUtcOffset: TimeSpan.Zero).ToUniversalTime();

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
                queryParameters.Add(Tuple.Create(SearchParameterNames.LastUpdated, at.ToString()));
            }
            else
            {
                if (since != null)
                {
                    queryParameters.Add(Tuple.Create(SearchParameterNames.LastUpdated, $"ge{since}"));

                    // when since is specified without _before, we will assume a value for before that is
                    // Now - <buffer>  the buffer value allows us to have confidence that ongoing
                    // edits are captured should the _before value be used in a future _history query

                    if (before == null)
                    {
                        addedBefore = Clock.UtcNow.AddSeconds(historyCurrentTimeBufferInSeconds);
                        queryParameters.Add(Tuple.Create(SearchParameterNames.LastUpdated, $"lt{addedBefore.Value.ToString("o")}"));
                    }
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

            SearchOptions searchOptions =
                !string.IsNullOrEmpty(resourceType)
                    ? _searchOptionsFactory.Create(resourceType, queryParameters)
                    : _searchOptionsFactory.Create(queryParameters);

            SearchResult searchResult = await SearchHistoryInternalAsync(searchOptions, cancellationToken);

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

            return _bundleFactory.CreateHistoryBundle(
                unsupportedSearchParams: null,
                result: searchResult,
                addedBefore);
        }

        /// <summary>
        /// Performs the actual search.
        /// </summary>
        /// <param name="searchOptions">The options to use during the search.</param>
        /// <param name="cancellationToken">The cancellationToken.</param>
        /// <returns>The search result.</returns>
        protected abstract Task<SearchResult> SearchInternalAsync(
            SearchOptions searchOptions,
            CancellationToken cancellationToken);

        protected abstract Task<SearchResult> SearchHistoryInternalAsync(
            SearchOptions searchOptions,
            CancellationToken cancellationToken);

        public void Build(ListedCapabilityStatement statement)
        {
            foreach (var resource in ModelInfo.SupportedResources)
            {
                var resourceType = ModelInfo.FhirTypeNameToResourceType(resource).GetValueOrDefault();

                statement.TryAddRestInteraction(resourceType, CapabilityStatement.TypeRestfulInteraction.HistoryType);
                statement.TryAddRestInteraction(resourceType, CapabilityStatement.TypeRestfulInteraction.HistoryInstance);
            }

            var restComponent = statement
                .Rest
                .Single();

            if (restComponent.Interaction == null)
            {
                restComponent.Interaction = new List<CapabilityStatement.SystemInteractionComponent>();
            }

            restComponent.Interaction.Add(new CapabilityStatement.SystemInteractionComponent
            {
                Code = CapabilityStatement.SystemRestfulInteraction.HistorySystem,
            });
        }
    }
}
