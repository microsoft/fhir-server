// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition.BundleWrappers;
using Microsoft.Health.Fhir.Core.Features.Health;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Messages.CapabilityStatement;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Messages.Storage;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Definition
{
    /// <summary>
    /// Provides mechanism to access search parameter definition.
    /// </summary>
    public class SearchParameterDefinitionManager : ISearchParameterDefinitionManager, INotificationHandler<SearchParametersUpdatedNotification>, INotificationHandler<StorageInitializedNotification>
    {
        private readonly IModelInfoProvider _modelInfoProvider;
        private readonly IMediator _mediator;
        private readonly ConcurrentDictionary<string, string> _resourceTypeSearchParameterHashMap;
        private readonly IScopeProvider<ISearchService> _searchServiceFactory;
        private readonly ISearchParameterComparer<SearchParameterInfo> _searchParameterComparer;
        private readonly IScopeProvider<ISearchParameterStatusDataStore> _searchParameterStatusDataStoreFactory;
        private readonly IScopeProvider<IFhirDataStore> _fhirDataStoreFactory;
        private readonly ILogger _logger;

        private bool _initialized = false;

        public SearchParameterDefinitionManager(
            IModelInfoProvider modelInfoProvider,
            IMediator mediator,
            IScopeProvider<ISearchService> searchServiceFactory,
            ISearchParameterComparer<SearchParameterInfo> searchParameterComparer,
            IScopeProvider<ISearchParameterStatusDataStore> searchParameterStatusDataStoreFactory,
            IScopeProvider<IFhirDataStore> fhirDataStoreFactory,
            ILogger<SearchParameterDefinitionManager> logger)
        {
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(searchServiceFactory, nameof(searchServiceFactory));
            EnsureArg.IsNotNull(searchParameterComparer, nameof(searchParameterComparer));
            EnsureArg.IsNotNull(searchParameterStatusDataStoreFactory, nameof(searchParameterStatusDataStoreFactory));
            EnsureArg.IsNotNull(fhirDataStoreFactory, nameof(fhirDataStoreFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _modelInfoProvider = modelInfoProvider;
            _mediator = mediator;
            _resourceTypeSearchParameterHashMap = new ConcurrentDictionary<string, string>();
            TypeLookup = new ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentQueue<SearchParameterInfo>>>();
            UrlLookup = new ConcurrentDictionary<string, SearchParameterInfo>();
            _searchServiceFactory = searchServiceFactory;
            _searchParameterComparer = searchParameterComparer;
            _searchParameterStatusDataStoreFactory = searchParameterStatusDataStoreFactory;
            _fhirDataStoreFactory = fhirDataStoreFactory;
            _logger = logger;

            var bundle = SearchParameterDefinitionBuilder.ReadEmbeddedSearchParameters("search-parameters.json", _modelInfoProvider);

            SearchParameterDefinitionBuilder.Build(
                bundle.Entries.Select(e => e.Resource).ToList(),
                UrlLookup,
                TypeLookup,
                _modelInfoProvider,
                _searchParameterComparer,
                _logger);
        }

        internal ConcurrentDictionary<string, SearchParameterInfo> UrlLookup { get; set; }

        // TypeLookup key is: Resource type, the inner dictionary key is the Search Parameter code.
        internal ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentQueue<SearchParameterInfo>>> TypeLookup { get; set; }

        public IEnumerable<SearchParameterInfo> AllSearchParameters => UrlLookup.Values;

        public IReadOnlyDictionary<string, string> SearchParameterHashMap
        {
            get { return new ReadOnlyDictionary<string, string>(_resourceTypeSearchParameterHashMap.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)); }
        }

        public async Task EnsureInitializedAsync(CancellationToken cancellationToken)
        {
            try
            {
                _initialized = true;
                await LoadSearchParamsFromDataStore(cancellationToken);

                await _mediator.Publish(new SearchParameterDefinitionManagerInitialized(), cancellationToken);
            }
            catch
            {
                _initialized = false;
                throw;
            }
        }

        public void EnsureInitialized()
        {
            if (!_initialized)
            {
                _logger.LogWarning("Search parameters are not initialized.");

                // throw new InitializationException("Failed to initialize search parameters");
            }
        }

        public IEnumerable<SearchParameterInfo> GetSearchParameters(string resourceType)
        {
            EnsureInitialized();

            if (TypeLookup.TryGetValue(resourceType, out ConcurrentDictionary<string, ConcurrentQueue<SearchParameterInfo>> value))
            {
                return value.Values.SelectMany(x => x).ToList();
            }

            throw new ResourceNotSupportedException(resourceType);
        }

        public SearchParameterInfo GetSearchParameter(string resourceType, string code)
        {
            EnsureInitialized();

            if (TypeLookup.TryGetValue(resourceType, out ConcurrentDictionary<string, ConcurrentQueue<SearchParameterInfo>> lookup) &&
                lookup.TryGetValue(code, out ConcurrentQueue<SearchParameterInfo> q) &&
                q.TryPeek(out var searchParameter))
            {
                return searchParameter;
            }

            throw new SearchParameterNotSupportedException(resourceType, code);
        }

        public bool TryGetSearchParameter(string resourceType, string code, out SearchParameterInfo searchParameter)
        {
            EnsureInitialized();
            searchParameter = null;

            if (TypeLookup.TryGetValue(resourceType, out ConcurrentDictionary<string, ConcurrentQueue<SearchParameterInfo>> lookup) &&
                lookup.TryGetValue(code, out ConcurrentQueue<SearchParameterInfo> q) &&
                q.TryPeek(out searchParameter))
            {
                return true;
            }

            return false;
        }

        public bool TryGetSearchParameter(string resourceType, string code, bool excludePendingDelete, out SearchParameterInfo searchParameter)
        {
            EnsureInitialized();
            searchParameter = null;

            if (TypeLookup.TryGetValue(resourceType, out ConcurrentDictionary<string, ConcurrentQueue<SearchParameterInfo>> lookup) &&
                lookup.TryGetValue(code, out ConcurrentQueue<SearchParameterInfo> q) &&
                q.TryPeek(out searchParameter))
            {
                if (excludePendingDelete && searchParameter.SearchParameterStatus == SearchParameterStatus.PendingDelete)
                {
                    searchParameter = null;
                    return false;
                }

                return true;
            }

            return false;
        }

        public SearchParameterInfo GetSearchParameter(string definitionUri)
        {
            EnsureInitialized();
            if (UrlLookup.TryGetValue(definitionUri, out SearchParameterInfo value))
            {
                return value;
            }

            throw new SearchParameterNotSupportedException(definitionUri);
        }

        public bool TryGetSearchParameter(string definitionUri, out SearchParameterInfo value)
        {
            EnsureInitialized();
            return UrlLookup.TryGetValue(definitionUri, out value);
        }

        public bool TryGetSearchParameter(string definitionUri, bool excludePendingDelete, out SearchParameterInfo value)
        {
            EnsureInitialized();
            if (UrlLookup.TryGetValue(definitionUri, out value))
            {
                if (excludePendingDelete && value.SearchParameterStatus == SearchParameterStatus.PendingDelete)
                {
                    value = null;
                    return false;
                }

                return true;
            }

            return false;
        }

        public string GetSearchParameterHashForResourceType(string resourceType)
        {
            EnsureInitialized();
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

        public void AddNewSearchParameters(IReadOnlyCollection<ITypedElement> searchParameters, bool calculateHash = true)
        {
            SearchParameterDefinitionBuilder.Build(
                searchParameters,
                UrlLookup,
                TypeLookup,
                _modelInfoProvider,
                _searchParameterComparer,
                _logger);

            if (calculateHash)
            {
                CalculateSearchParameterHash();
            }
        }

        private void CalculateSearchParameterHash()
        {
            foreach (string resourceName in TypeLookup.Keys)
            {
                var searchParameters = TypeLookup[resourceName].Values.SelectMany(x => x);
                if (searchParameters.Any())
                {
                    string searchParamHash = searchParameters.CalculateSearchParameterHash();
                    _resourceTypeSearchParameterHashMap.AddOrUpdate(
                        resourceName,
                        searchParamHash,
                        (resourceType, existingValue) => searchParamHash);
                }
            }
        }

        public void DeleteSearchParameter(ITypedElement searchParam)
        {
            var searchParamWrapper = new SearchParameterWrapper(searchParam);
            DeleteSearchParameter(searchParamWrapper.Url);
        }

        public void DeleteSearchParameter(string url, bool calculateHash = true)
        {
            SearchParameterInfo searchParameterInfo = null;

            if (!UrlLookup.TryRemove(url, out searchParameterInfo))
            {
                throw new ResourceNotFoundException(string.Format(Core.Resources.CustomSearchParameterNotfound, url));
            }

            // find all derived resources from the list of base resources
            var allResourceTypes = GetDerivedResourceTypes(searchParameterInfo.BaseResourceTypes);
            var updated = false;
            foreach (var resourceType in allResourceTypes)
            {
                if (TypeLookup.TryGetValue(resourceType, out var lookup) &&
                    lookup.TryGetValue(searchParameterInfo.Code, out var q) &&
                    q.Any(x => string.Equals(x.Url.OriginalString, url, StringComparison.OrdinalIgnoreCase)))
                {
                    var newq = new ConcurrentQueue<SearchParameterInfo>(q.Where(x => !string.Equals(x.Url.OriginalString, url, StringComparison.OrdinalIgnoreCase)));
                    if (lookup.TryUpdate(searchParameterInfo.Code, newq, q))
                    {
                        updated = true;
                    }
                    else
                    {
                        _logger.LogError("Failed to remove a search parameter from TypeLookup: {Url}, {ResourceType}, {Code}", url, resourceType, searchParameterInfo.Code);
                    }
                }
            }

            if (calculateHash && updated)
            {
                CalculateSearchParameterHash();
            }
        }

        public void UpdateSearchParameterStatus(string url, SearchParameterStatus desiredStatus)
        {
            if (UrlLookup.TryGetValue(url, out var searchParameterInfo))
            {
                searchParameterInfo.SearchParameterStatus = desiredStatus;
            }
        }

        public async Task Handle(SearchParametersUpdatedNotification notification, CancellationToken cancellationToken)
        {
            var retry = 0;
            while (retry < 3)
            {
                try
                {
                    _logger.LogInformation("SearchParameterDefinitionManager: Search parameters updated");
                    CalculateSearchParameterHash();
                    await _mediator.Publish(new RebuildCapabilityStatement(RebuildPart.SearchParameter), cancellationToken);
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error calculating search parameter hash. Retry {retry}");
                    retry++;
                }
            }

            // Not reporting notification while we investigate why this could happen
            // await _mediator.Publish(new ImproperBehaviorNotification("Error calculating search parameter hash"), cancellationToken);
        }

        public async Task Handle(StorageInitializedNotification notification, CancellationToken cancellationToken)
        {
            var retry = 0;
            while (retry < 3)
            {
                try
                {
                    _logger.LogInformation("SearchParameterDefinitionManager: Storage initialized");
                    await EnsureInitializedAsync(cancellationToken);
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error initializing search parameters. Retry {retry}");
                    retry++;
                }
            }

            // Not reporting notification while we investigate why this could happen
            // await _mediator.Publish(new ImproperBehaviorNotification("Error initializing search parameters"), cancellationToken);
        }

        private async Task LoadSearchParamsFromDataStore(CancellationToken cancellationToken)
        {
            // now read in any previously POST'd SearchParameter resources
            using IScoped<ISearchService> search = _searchServiceFactory.Invoke();
            using IScoped<ISearchParameterStatusDataStore> statusDataStore = _searchParameterStatusDataStoreFactory.Invoke();
            using IScoped<IFhirDataStore> fhirDataStore = _fhirDataStoreFactory.Invoke();

            string continuationToken = null;
            int totalLoaded = 0;
            int totalPendingDelete = 0;

            // Get all PendingDelete search parameters from the status store
            var allStatuses = await statusDataStore.Value.GetSearchParameterStatuses(cancellationToken);
            var pendingDeleteUrls = new HashSet<string>(
                allStatuses
                    .Where(s => s.Status == SearchParameterStatus.PendingDelete)
                    .Select(s => s.Uri.OriginalString),
                StringComparer.OrdinalIgnoreCase);

            _logger.LogInformation(
                "Found {PendingDeleteCount} search parameters with PendingDelete status in the status store",
                pendingDeleteUrls.Count);

            do
            {
                var searchOptions = new SearchOptions();
                searchOptions.Sort = new List<(SearchParameterInfo, SortOrder)>();
                searchOptions.UnsupportedSearchParams = new List<Tuple<string, string>>();
                searchOptions.Expression = Expression.SearchParameter(
                    SearchParameterInfo.ResourceTypeSearchParameter,
                    Expression.StringEquals(FieldName.TokenCode, null, KnownResourceTypes.SearchParameter, false));
                searchOptions.MaxItemCount = 10;

                // ✅ Include soft-deleted resources to find PendingDelete search parameters
                searchOptions.ResourceVersionTypes = ResourceVersionType.Latest | ResourceVersionType.SoftDeleted;

                if (continuationToken != null)
                {
                    searchOptions.ContinuationToken = continuationToken;
                }

                var result = await search.Value.SearchAsync(searchOptions, cancellationToken);
                continuationToken = result?.ContinuationToken;

                if (result?.Results != null && result.Results.Any())
                {
                    foreach (var searchResult in result.Results)
                    {
                        var isDeleted = searchResult.Resource.IsDeleted;

                        // For soft-deleted resources, check if they are in PendingDelete status
                        if (isDeleted)
                        {
                            try
                            {
                                // Get the resource ID to fetch its last version before deletion
                                var resourceId = searchResult.Resource.ResourceId;

                                // Parse the current version and calculate the previous version
                                if (int.TryParse(searchResult.Resource.Version, out int currentVersion) && currentVersion > 1)
                                {
                                    var previousVersion = (currentVersion - 1).ToString();
                                    var resourceKey = new ResourceKey(KnownResourceTypes.SearchParameter, resourceId, previousVersion);
                                    var lastVersion = await fhirDataStore.Value.GetAsync(resourceKey, cancellationToken);

                                    if (lastVersion?.RawResource != null)
                                    {
                                        var searchParam = lastVersion.RawResource.ToITypedElement(_modelInfoProvider);
                                        var urlScalar = searchParam.GetStringScalar("url");

                                        // Only load if this URL is marked as PendingDelete in the status store
                                        if (!string.IsNullOrEmpty(urlScalar) && pendingDeleteUrls.Contains(urlScalar))
                                        {
                                            // Build the search parameter using the last version before deletion
                                            SearchParameterDefinitionBuilder.Build(
                                                new List<ITypedElement>() { searchParam },
                                                UrlLookup,
                                                TypeLookup,
                                                _modelInfoProvider,
                                                _searchParameterComparer,
                                                _logger);

                                            totalLoaded++;

                                            // Update the status to PendingDelete since the resource is soft-deleted
                                            if (UrlLookup.TryGetValue(urlScalar, out var loadedParam))
                                            {
                                                loadedParam.SearchParameterStatus = SearchParameterStatus.PendingDelete;
                                                totalPendingDelete++;
                                                _logger.LogInformation(
                                                    "Loaded PendingDelete search parameter from last version before deletion: {Url}",
                                                    urlScalar);
                                            }
                                        }
                                        else if (!string.IsNullOrEmpty(urlScalar))
                                        {
                                            _logger.LogDebug(
                                                "Skipping soft-deleted SearchParameter {ResourceId} with URL {Url} - not in PendingDelete status",
                                                resourceId,
                                                urlScalar);
                                        }
                                        else
                                        {
                                            _logger.LogWarning(
                                                "Could not retrieve valid URL for soft-deleted SearchParameter {ResourceId}",
                                                resourceId);
                                        }
                                    }
                                    else
                                    {
                                        _logger.LogWarning(
                                            "Could not retrieve last version for soft-deleted SearchParameter {ResourceId}",
                                            resourceId);
                                    }
                                }
                                else
                                {
                                    _logger.LogWarning(
                                        "Could not parse version or version is 1 for soft-deleted SearchParameter {ResourceId}, version: {Version}",
                                        resourceId,
                                        searchResult.Resource.Version);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(
                                    ex,
                                    "Error loading last version of soft-deleted SearchParameter {ResourceId}",
                                    searchResult.Resource.ResourceId);
                            }
                        }
                        else
                        {
                            // Normal processing for active resources
                            var searchParam = searchResult.Resource.RawResource.ToITypedElement(_modelInfoProvider);

                            try
                            {
                                SearchParameterDefinitionBuilder.Build(
                                    new List<ITypedElement>() { searchParam },
                                    UrlLookup,
                                    TypeLookup,
                                    _modelInfoProvider,
                                    _searchParameterComparer,
                                    _logger);

                                totalLoaded++;
                            }
                            catch (FhirException ex)
                            {
                                StringBuilder issueDetails = new StringBuilder();
                                foreach (OperationOutcomeIssue issue in ex.Issues)
                                {
                                    issueDetails.Append(issue.Diagnostics).Append("; ");
                                }

                                _logger.LogWarning(
                                    ex,
                                    "Error loading search parameter {Url} from data store. Issues: {Issues}",
                                    searchParam.GetStringScalar("url"),
                                    issueDetails.ToString());
                            }
                            catch (Exception ex) when (
                                !(ex is OutOfMemoryException
                                || ex is StackOverflowException
                                || ex is ThreadAbortException))
                            {
                                _logger.LogError(
                                    ex,
                                    "Error loading search parameter {Url} from data store.",
                                    searchParam.GetStringScalar("url"));
                            }
                        }
                    }
                }
            }
            while (continuationToken != null);

            _logger.LogInformation(
                "Loaded {TotalLoaded} active and {TotalPendingDelete} PendingDelete search parameters from data store",
                totalLoaded,
                totalPendingDelete);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1859:Use concrete types when possible for improved performance", Justification = "Collection defined on model")]
        private ICollection<string> GetDerivedResourceTypes(IReadOnlyCollection<string> resourceTypes)
        {
            var completeResourceList = new HashSet<string>(resourceTypes);

            foreach (var baseResourceType in resourceTypes)
            {
                if (baseResourceType == KnownResourceTypes.Resource)
                {
                    completeResourceList.UnionWith(_modelInfoProvider.GetResourceTypeNames().ToHashSet());

                    // We added all possible resource types, so no need to continue
                    break;
                }

                if (baseResourceType == KnownResourceTypes.DomainResource)
                {
                    var domainResourceChildResourceTypes = _modelInfoProvider.GetResourceTypeNames().ToHashSet();

                    // Remove types that inherit from Resource directly
                    domainResourceChildResourceTypes.Remove(KnownResourceTypes.Binary);
                    domainResourceChildResourceTypes.Remove(KnownResourceTypes.Bundle);
                    domainResourceChildResourceTypes.Remove(KnownResourceTypes.Parameters);

                    completeResourceList.UnionWith(domainResourceChildResourceTypes);
                }
            }

            return completeResourceList;
        }
    }
}
