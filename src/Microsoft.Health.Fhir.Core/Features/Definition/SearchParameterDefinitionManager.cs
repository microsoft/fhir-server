using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Build.Framework;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition.BundleWrappers;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Messages.Storage;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Definition
{
    internal class SearchParameterDefinitionManager : ISearchParameterDefinitionManager
    {
        private readonly IModelInfoProvider _modelInfoProvider;
        private ISearchParameterStatusDataStore _searchParameterStatusDataStore;
        private ISearchService _searchService;
        private ILogger<SearchParameterDefinitionManager> _logger;
        private ConcurrentDictionary<string, SearchParameterInfo> _searchParameterCache = new ConcurrentDictionary<string, SearchParameterInfo>();
        private ConcurrentDictionary<string, List<string>> _searchParameterUriByResourceTypeCache = new ConcurrentDictionary<string, List<string>>();
        private ConcurrentDictionary<SearchParameterStatus, List<string>> _searchParameterUriByStatusCache = new ConcurrentDictionary<SearchParameterStatus, List<string>>();
        private DateTime _lastUpdated = DateTime.MinValue;
        private int _refreshTimeInSeconds = 20;
        private int _searchParameterFetchBatchSize = 100;

        public SearchParameterDefinitionManager(
            ISearchParameterStatusDataStore searchParameterStatusDataStore,
            ISearchService searchService,
            IModelInfoProvider modelInfoProvider,
            ILogger<SearchParameterDefinitionManager> logger)
        {
            _searchParameterStatusDataStore = EnsureArg.IsNotNull(searchParameterStatusDataStore, nameof(searchParameterStatusDataStore));
            _searchService = EnsureArg.IsNotNull(searchService, nameof(searchService));
            _modelInfoProvider = EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));

            var bundle = SearchParameterDefinitionBuilder.ReadEmbeddedSearchParameters("search-parameters.json", _modelInfoProvider);
            var entries = bundle.Entries.Select(e => e.Resource).ToList();

            foreach (var param in entries)
            {
                var info = new SearchParameterInfo(new SearchParameterWrapper(param));
                _searchParameterCache.AddOrUpdate(
                    info.Url.ToString(),
                    (key) => info,
                    (key, oldValue) => info);
            }
        }

        public IEnumerable<SearchParameterInfo> AllSearchParameters => _searchParameterCache.Values;

        public IReadOnlyDictionary<string, string> SearchParameterHashMap { get; }

        public async Task Handle(StorageInitializedNotification notification, CancellationToken cancellationToken)
        {
            await Refresh(cancellationToken);
        }

        public IEnumerable<SearchParameterInfo> GetSearchParameters(string resourceType)
        {
            if (_searchParameterUriByResourceTypeCache.TryGetValue(resourceType, out List<string> searchParameterUris))
            {
                return searchParameterUris.Select(uri => _searchParameterCache[uri]);
            }

            return Enumerable.Empty<SearchParameterInfo>();
        }

        public IEnumerable<SearchParameterInfo> GetSearchParameters(SearchParameterStatus status)
        {
            if (_searchParameterUriByStatusCache.TryGetValue(status, out List<string> searchParameterUris))
            {
                return searchParameterUris.Select(uri => _searchParameterCache[uri]);
            }

            return Enumerable.Empty<SearchParameterInfo>();
        }

        public bool TryGetSearchParameter(string definitionUri, bool excludePendingDelete, out SearchParameterInfo searchParameter)
        {
            if (_searchParameterCache.TryGetValue(definitionUri, out SearchParameterInfo info))
            {
                if (!excludePendingDelete || info.SearchParameterStatus != SearchParameterStatus.PendingDelete)
                {
                    searchParameter = info;
                    return true;
                }
            }

            searchParameter = null;
            return false;
        }

        public bool TryGetSearchParameter(string definitionUri, out SearchParameterInfo searchParameter)
        {
            return TryGetSearchParameter(definitionUri, excludePendingDelete: false, searchParameter: out searchParameter);
        }

        public bool TryGetSearchParameter(string resourceType, string code, bool excludePendingDelete, out SearchParameterInfo searchParameter)
        {
            if (_searchParameterUriByResourceTypeCache.TryGetValue(resourceType, out List<string> searchParameterUris))
            {
                foreach (var uri in searchParameterUris)
                {
                    if (_searchParameterCache.TryGetValue(uri, out SearchParameterInfo info))
                    {
                        if (string.Equals(info.Code, code, StringComparison.OrdinalIgnoreCase) &&
                            (!excludePendingDelete || info.SearchParameterStatus != SearchParameterStatus.PendingDelete))
                        {
                            searchParameter = info;
                            return true;
                        }
                    }
                }
            }

            searchParameter = null;
            return false;
        }

        public bool TryGetSearchParameter(string resourceType, string code, out SearchParameterInfo searchParameter)
        {
            return TryGetSearchParameter(resourceType, code, excludePendingDelete: false, searchParameter: out searchParameter);
        }

        public SearchParameterInfo GetSearchParameter(string resourceType, string code)
        {
            if (TryGetSearchParameter(resourceType, code, out SearchParameterInfo searchParameter))
            {
                return searchParameter;
            }

            throw new KeyNotFoundException(string.Format(Core.Resources.SearchParameterDefinitionNotFound, code, resourceType));
        }

        public SearchParameterInfo GetSearchParameter(string definitionUri)
        {
            if (TryGetSearchParameter(definitionUri, excludePendingDelete: false, out SearchParameterInfo searchParameter))
            {
                return searchParameter;
            }

            throw new KeyNotFoundException(string.Format(Core.Resources.SearchParameterDefinitionNotFound, definitionUri));
        }

        public string GetSearchParameterHashForResourceType(string resourceType)
        {
            throw new NotImplementedException();
        }

        public async Task ForceRefresh(CancellationToken cancellationToken)
        {
            await Refresh(cancellationToken);
        }

        private async Task Refresh(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Refreshing search parameter definitions.");
            var searchParameters = await _searchParameterStatusDataStore.GetSearchParameterStatuses(cancellationToken, _lastUpdated);
            _lastUpdated = DateTime.UtcNow - new TimeSpan(0, 0, _refreshTimeInSeconds);

            var searchParameterBatch = new List<string>();
            var searchParameterDetails = new List<SearchParameterInfo>();
            foreach (var searchParameter in searchParameters)
            {
                searchParameterBatch.Add(searchParameter.Uri.ToString());
                if (searchParameterBatch.Count >= _searchParameterFetchBatchSize)
                {
                    _logger.LogInformation("Refreshing search parameter definitions. Fetching details for batch of {Count} search parameters.", searchParameterBatch.Count);
                    searchParameterDetails.AddRange(await FetchSearchParameterDetails(searchParameterBatch, cancellationToken));
                    searchParameterBatch.Clear();
                }
            }

            foreach (var searchParameter in searchParameters.Where((param) => param.Status != SearchParameterStatus.Deleted && param.Status != SearchParameterStatus.PendingDelete))
            {
                var details = searchParameterDetails.FirstOrDefault(s => s.Url == searchParameter.Uri);
                if (details == null)
                {
                    _logger.LogError("No details found for search param with Uri {Uri}.", searchParameter.Uri);
                    throw new InvalidOperationException($"No details found for search param with Uri {searchParameter.Uri}.");
                }

                details.SearchParameterStatus = searchParameter.Status;
                details.SortStatus = searchParameter.SortStatus;
                details.IsPartiallySupported = searchParameter.IsPartiallySupported;

                _searchParameterCache[searchParameter.Uri.ToString()] = details;
            }

            foreach (var searchParameter in searchParameters.Where((param) => param.Status == SearchParameterStatus.Deleted || param.Status == SearchParameterStatus.PendingDelete))
            {
                _searchParameterCache.Remove(searchParameter.Uri.ToString(), out _);
            }

            UpdateIndexDictionaries();
            _logger.LogInformation("Finished refreshing search parameter definitions.");
        }

        private async Task<IEnumerable<SearchParameterInfo>> FetchSearchParameterDetails(List<string> searchParameterBatch, CancellationToken cancellationToken)
        {
            List<Tuple<string, string>> queryParameters = new List<Tuple<string, string>>();
            queryParameters.Add(Tuple.Create(KnownQueryParameterNames.Count, _searchParameterFetchBatchSize.ToString()));
            queryParameters.Add(Tuple.Create("url", string.Join(",", searchParameterBatch)));

            var result = await _searchService.SearchAsync(
                KnownResourceTypes.SearchParameter,
                queryParameters,
                cancellationToken);

            return result.Results.Select(r =>
            {
                return new SearchParameterInfo(new SearchParameterWrapper(r.Resource.RawResource.ToITypedElement(_modelInfoProvider)));
            });
        }

        private void UpdateIndexDictionaries()
        {
            _searchParameterUriByResourceTypeCache.Clear();
            _searchParameterUriByStatusCache.Clear();
            foreach (var searchParameterInfo in _searchParameterCache.Values)
            {
                foreach (var resourceType in searchParameterInfo.TargetResourceTypes)
                {
                    _searchParameterUriByResourceTypeCache.AddOrUpdate(resourceType, new List<string>() { searchParameterInfo.Url.ToString() }, (key, oldValue) =>
                    {
                        oldValue.Add(searchParameterInfo.Url.ToString());
                        return oldValue;
                    });
                }

                _searchParameterUriByStatusCache.AddOrUpdate(searchParameterInfo.SearchParameterStatus, new List<string>() { searchParameterInfo.Url.ToString() }, (key, oldValue) =>
                {
                    oldValue.Add(searchParameterInfo.Url.ToString());
                    return oldValue;
                });
            }
        }
    }
}
