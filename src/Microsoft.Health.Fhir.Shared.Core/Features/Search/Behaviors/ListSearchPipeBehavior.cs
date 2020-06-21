// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Behavior
{
    /// <summary>
    /// This behavior looks for searches that use _list then resolves the list and rewrites the query with the resource _ids
    /// </summary>
    public class ListSearchPipeBehavior : IPipelineBehavior<SearchResourceRequest, SearchResourceResponse>
    {
        private const string _listParameter = "_list";
        private const string _idParameter = "_id";
        private readonly IScoped<IFhirDataStore> _dataStore;
        private readonly ResourceDeserializer _deserializer;
        private readonly IBundleFactory _bundleFactory;
        private readonly IReferenceSearchValueParser _referenceSearchValueParser;
        private readonly ISearchOptionsFactory _searchOptionsFactory;

        public ListSearchPipeBehavior(
            ISearchOptionsFactory searchOptionsFactory,
            IBundleFactory bundleFactory,
            IScoped<IFhirDataStore> dataStore,
            ResourceDeserializer deserializer,
            IReferenceSearchValueParser referenceSearchValueParser)
        {
            EnsureArg.IsNotNull(bundleFactory, nameof(bundleFactory));
            EnsureArg.IsNotNull(dataStore, nameof(dataStore));
            EnsureArg.IsNotNull(deserializer, nameof(deserializer));
            EnsureArg.IsNotNull(referenceSearchValueParser, nameof(referenceSearchValueParser));
            EnsureArg.IsNotNull(searchOptionsFactory, nameof(searchOptionsFactory));

            _searchOptionsFactory = searchOptionsFactory;
            _bundleFactory = bundleFactory;
            _dataStore = dataStore;
            _deserializer = deserializer;
            _referenceSearchValueParser = referenceSearchValueParser;
        }

        public async Task<SearchResourceResponse> Handle(SearchResourceRequest request, CancellationToken cancellationToken, RequestHandlerDelegate<SearchResourceResponse> next)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            Tuple<string, string> listParameter = request.Queries
                .FirstOrDefault(x => string.Equals(x.Item1, _listParameter, StringComparison.Ordinal));

            // if _list was not requested, continue...
            if (listParameter == null)
            {
                return await next();
            }

            // create an empty response to return if needed
            SearchResourceResponse emptyResponse = CreateEmptySearchResponse(request);

            // Remove the 'list' params from the queries, to be later replaced with specific ids of resources
            IEnumerable<Tuple<string, string>> query = request.Queries.Except(new[] { listParameter });

            // if _list was requested with invalid value. continue
            if (string.IsNullOrWhiteSpace(listParameter.Item2))
            {
                return emptyResponse;
            }

            ResourceWrapper listWrapper =
                await _dataStore.Value.GetAsync(new ResourceKey<Hl7.Fhir.Model.List>(listParameter.Item2), cancellationToken);

            // wanted list was not found
            if (listWrapper == null)
            {
                return emptyResponse;
            }

            ResourceElement list = _deserializer.Deserialize(listWrapper);
            IEnumerable<ReferenceSearchValue> references = list.ToPoco<Hl7.Fhir.Model.List>()
                .Entry
                .Where(x => x.Deleted != true)
                .Select(x => _referenceSearchValueParser.Parse(x.Item.Reference))
                .Where(x => string.IsNullOrWhiteSpace(request.ResourceType) ||
                            string.Equals(request.ResourceType, x.ResourceType, StringComparison.Ordinal))
                .ToArray();

            // the requested resource was not found in the list
            if (!references.Any())
            {
                return emptyResponse;
            }

            query = query.Concat(new[] { Tuple.Create(_idParameter, string.Join(",", references.Select(x => x.ResourceId))) });
            request.Queries = query.ToArray();
            return await next();
        }

        public SearchResourceResponse CreateEmptySearchResponse(SearchResourceRequest request)
        {
            SearchOptions searchOptions = _searchOptionsFactory.Create(request.ResourceType, request.Queries);
            IReadOnlyList<(string parameterName, string reason)> unsupportedSortingParameters;
            if (searchOptions.Sort?.Count > 0)
            {
                // we don't currently support sort
                unsupportedSortingParameters = searchOptions.UnsupportedSortingParams.Concat(searchOptions.Sort.Select(s => (s.searchParameterInfo.Name, Core.Resources.SortNotSupported))).ToList();
            }
            else
            {
                unsupportedSortingParameters = searchOptions.UnsupportedSortingParams;
            }

            SearchResult emptySearchResult =
                new SearchResult(new List<SearchResultEntry>(), searchOptions.UnsupportedSearchParams, unsupportedSortingParameters, null);

            ResourceElement bundle = _bundleFactory.CreateSearchBundle(emptySearchResult);
            return new SearchResourceResponse(bundle);
        }
    }
}