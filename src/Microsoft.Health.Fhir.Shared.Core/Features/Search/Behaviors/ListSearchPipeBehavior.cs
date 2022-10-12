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

        public async Task<SearchResourceResponse> Handle(SearchResourceRequest request, RequestHandlerDelegate<SearchResourceResponse> next, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            Tuple<string, string> listParameter = request.Queries
                .FirstOrDefault(x => string.Equals(x.Item1, KnownQueryParameterNames.List, StringComparison.OrdinalIgnoreCase));

            // if _list was not requested, or _list was requested with invalid value, continue...
            if ((listParameter == null) || string.IsNullOrWhiteSpace(listParameter.Item2))
            {
                return await next();
            }

            ResourceWrapper listWrapper =
                await _dataStore.Value.GetAsync(new ResourceKey<Hl7.Fhir.Model.List>(listParameter.Item2), cancellationToken);

            // wanted list was not found
            if (listWrapper == null)
            {
                return CreateEmptySearchResponse(request);
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
                return CreateEmptySearchResponse(request);
            }

            // Remove the 'list' params from the queries, to be later replaced with specific ids of resources
            IEnumerable<Tuple<string, string>> query = request.Queries.Except(new[] { listParameter });

            query = query.Concat(new[] { Tuple.Create(KnownQueryParameterNames.Id, string.Join(",", references.Select(x => x.ResourceId))) });
            request.Queries = query.ToArray();
            return await next();
        }

        public SearchResourceResponse CreateEmptySearchResponse(SearchResourceRequest request)
        {
            SearchOptions searchOptions = _searchOptionsFactory.Create(request.ResourceType, request.Queries);

            SearchResult emptySearchResult = SearchResult.Empty(searchOptions.UnsupportedSearchParams);

            ResourceElement bundle = _bundleFactory.CreateSearchBundle(emptySearchResult);
            return new SearchResourceResponse(bundle);
        }
    }
}
