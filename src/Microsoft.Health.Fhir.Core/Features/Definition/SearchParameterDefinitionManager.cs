// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using MediatR;
using Microsoft.Extensions.Hosting;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition.BundleWrappers;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Definition
{
    /// <summary>
    /// Provides mechanism to access search parameter definition.
    /// </summary>
    public class SearchParameterDefinitionManager : ISearchParameterDefinitionManager, IHostedService, INotificationHandler<SearchParametersUpdated>
    {
        private readonly IModelInfoProvider _modelInfoProvider;
        private ConcurrentDictionary<string, string> _resourceTypeSearchParameterHashMap;

        // Note that the SearchService depends on the this class, the SearchParameterDefinitionManager
        // to provide information on SearchParameters.  Here this class uses the SearchService to query
        // all resources of type SearchParameter in order to successfully initialize
        // but the search service we use is not fully functional, it will only support limited searches
        private readonly Func<IScoped<ISearchService>> _searchServiceFactory;

        public SearchParameterDefinitionManager(IModelInfoProvider modelInfoProvider, Func<IScoped<ISearchService>> searchServiceFactory)
        {
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            EnsureArg.IsNotNull(searchServiceFactory, nameof(searchServiceFactory));

            _modelInfoProvider = modelInfoProvider;
            _resourceTypeSearchParameterHashMap = new ConcurrentDictionary<string, string>();
            TypeLookup = new ConcurrentDictionary<string, ConcurrentDictionary<string, SearchParameterInfo>>();
            UrlLookup = new ConcurrentDictionary<Uri, SearchParameterInfo>();
            _searchServiceFactory = searchServiceFactory;
        }

        internal ConcurrentDictionary<Uri, SearchParameterInfo> UrlLookup { get; set; }

        // TypeLookup key is: Resource type, the inner dictionary key is the Search Parameter code.
        internal ConcurrentDictionary<string, ConcurrentDictionary<string, SearchParameterInfo>> TypeLookup { get; }

        public IEnumerable<SearchParameterInfo> AllSearchParameters => UrlLookup.Values;

        public IReadOnlyDictionary<string, string> SearchParameterHashMap
        {
            get { return new ReadOnlyDictionary<string, string>(_resourceTypeSearchParameterHashMap.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)); }
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            // read in the spec defined search parameters from search-parameters.json
            var bundle = SearchParameterDefinitionBuilder.ReadEmbeddedSearchParameters("search-parameters.json", _modelInfoProvider);

            SearchParameterDefinitionBuilder.Build(
                bundle.Entries.Select(e => e.Resource).ToList(),
                UrlLookup,
                TypeLookup,
                _modelInfoProvider);

            // now read in any previously POST'd SearchParameter resources
            using (var search = _searchServiceFactory())
            {
                string continuationToken = null;
                do
                {
                    var queryParams = new List<Tuple<string, string>>();
                    if (continuationToken != null)
                    {
                        queryParams.Add(new Tuple<string, string>(KnownQueryParameterNames.ContinuationToken, continuationToken));
                    }

                    var result = await search.Value.SearchAsync("SearchParameter", queryParams, cancellationToken);
                    if (!string.IsNullOrEmpty(result?.ContinuationToken))
                    {
                        continuationToken = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(result.ContinuationToken));
                    }
                    else
                    {
                        continuationToken = null;
                    }

                    if (result != null && result.Results != null && result.Results.Count() > 0)
                    {
                        var searchParams = result.Results.Select(e => e.Resource.RawResource.ToITypedElement(_modelInfoProvider));

                        SearchParameterDefinitionBuilder.Build(
                            searchParams.ToList(),
                            UrlLookup,
                            TypeLookup,
                            _modelInfoProvider);
                    }
                }
                while (continuationToken != null);
            }

            CalculateSearchParameterHash();

            return;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public IEnumerable<SearchParameterInfo> GetSearchParameters(string resourceType)
        {
            if (TypeLookup.TryGetValue(resourceType, out ConcurrentDictionary<string, SearchParameterInfo> value))
            {
                return value.Values;
            }

            throw new ResourceNotSupportedException(resourceType);
        }

        public SearchParameterInfo GetSearchParameter(string resourceType, string code)
        {
            if (TypeLookup.TryGetValue(resourceType, out ConcurrentDictionary<string, SearchParameterInfo> lookup) &&
                lookup.TryGetValue(code, out SearchParameterInfo searchParameter))
            {
                return searchParameter;
            }

            throw new SearchParameterNotSupportedException(resourceType, code);
        }

        public bool TryGetSearchParameter(string resourceType, string code, out SearchParameterInfo searchParameter)
        {
            searchParameter = null;

            return TypeLookup.TryGetValue(resourceType, out ConcurrentDictionary<string, SearchParameterInfo> searchParameters) &&
                searchParameters.TryGetValue(code, out searchParameter);
        }

        public SearchParameterInfo GetSearchParameter(Uri definitionUri)
        {
            if (UrlLookup.TryGetValue(definitionUri, out SearchParameterInfo value))
            {
                return value;
            }

            throw new SearchParameterNotSupportedException(definitionUri);
        }

        public string GetSearchParameterHashForResourceType(string resourceType)
        {
            EnsureArg.IsNotNullOrWhiteSpace(resourceType, nameof(resourceType));

            if (_resourceTypeSearchParameterHashMap.TryGetValue(resourceType, out string hash))
            {
                return hash;
            }

            return null;
        }

        public void UpdateSearchParameterHashMap(Dictionary<string, string> updatedSearchParamHashMap)
        {
            EnsureArg.IsNotNull(updatedSearchParamHashMap, nameof(updatedSearchParamHashMap));

            foreach (KeyValuePair<string, string> kvp in updatedSearchParamHashMap)
            {
                _resourceTypeSearchParameterHashMap.AddOrUpdate(
                    kvp.Key,
                    kvp.Value,
                    (resourceType, existingValue) => kvp.Value);
            }
        }

        public void AddNewSearchParameters(IReadOnlyCollection<ITypedElement> searchParameters)
        {
            SearchParameterDefinitionBuilder.Build(
                searchParameters,
                UrlLookup,
                TypeLookup,
                _modelInfoProvider);

            CalculateSearchParameterHash();
        }

        private void CalculateSearchParameterHash()
        {
            foreach (string resourceName in TypeLookup.Keys)
            {
                string searchParamHash = TypeLookup[resourceName].Values.CalculateSearchParameterHash();
                _resourceTypeSearchParameterHashMap.AddOrUpdate(
                    resourceName,
                    searchParamHash,
                    (resourceType, existingValue) => searchParamHash);
            }
        }

        public void DeleteSearchParameter(ITypedElement searchParam)
        {
            var searchParamWrapper = new SearchParameterWrapper(searchParam);
            SearchParameterInfo searchParameterInfo = null;

            if (!UrlLookup.TryRemove(new Uri(searchParamWrapper.Url), out searchParameterInfo))
            {
                throw new Exception(string.Format(Resources.CustomSearchParameterNotfound, searchParamWrapper.Url));
            }

            var allResourceTypes = searchParameterInfo.TargetResourceTypes.Union(searchParameterInfo.BaseResourceTypes);
            foreach (var resourceType in allResourceTypes)
            {
                TypeLookup[resourceType].TryRemove(searchParameterInfo.Code, out var removedParam);
            }

            CalculateSearchParameterHash();
        }

        public Task Handle(SearchParametersUpdated notification, CancellationToken cancellationToken)
        {
            CalculateSearchParameterHash();

            return Task.CompletedTask;
        }
    }
}
