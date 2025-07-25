﻿// -------------------------------------------------------------------------------------------------
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
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
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
        private readonly ILogger _logger;

        private bool _initialized = false;

        public SearchParameterDefinitionManager(
            IModelInfoProvider modelInfoProvider,
            IMediator mediator,
            IScopeProvider<ISearchService> searchServiceFactory,
            ILogger<SearchParameterDefinitionManager> logger)
        {
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(searchServiceFactory, nameof(searchServiceFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _modelInfoProvider = modelInfoProvider;
            _mediator = mediator;
            _resourceTypeSearchParameterHashMap = new ConcurrentDictionary<string, string>();
            TypeLookup = new ConcurrentDictionary<string, ConcurrentDictionary<string, SearchParameterInfo>>();
            UrlLookup = new ConcurrentDictionary<string, SearchParameterInfo>();
            _searchServiceFactory = searchServiceFactory;
            _logger = logger;

            var bundle = SearchParameterDefinitionBuilder.ReadEmbeddedSearchParameters("search-parameters.json", _modelInfoProvider);

            SearchParameterDefinitionBuilder.Build(
                bundle.Entries.Select(e => e.Resource).ToList(),
                UrlLookup,
                TypeLookup,
                _modelInfoProvider,
                _logger);
        }

        internal ConcurrentDictionary<string, SearchParameterInfo> UrlLookup { get; set; }

        // TypeLookup key is: Resource type, the inner dictionary key is the Search Parameter code.
        internal ConcurrentDictionary<string, ConcurrentDictionary<string, SearchParameterInfo>> TypeLookup { get; set; }

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

            if (TypeLookup.TryGetValue(resourceType, out ConcurrentDictionary<string, SearchParameterInfo> value))
            {
                return value.Values;
            }

            throw new ResourceNotSupportedException(resourceType);
        }

        public SearchParameterInfo GetSearchParameter(string resourceType, string code)
        {
            EnsureInitialized();

            if (TypeLookup.TryGetValue(resourceType, out ConcurrentDictionary<string, SearchParameterInfo> lookup) &&
                lookup.TryGetValue(code, out SearchParameterInfo searchParameter))
            {
                return searchParameter;
            }

            throw new SearchParameterNotSupportedException(resourceType, code);
        }

        public bool TryGetSearchParameter(string resourceType, string code, out SearchParameterInfo searchParameter)
        {
            EnsureInitialized();
            searchParameter = null;

            return TypeLookup.TryGetValue(resourceType, out ConcurrentDictionary<string, SearchParameterInfo> searchParameters) &&
                searchParameters.TryGetValue(code, out searchParameter);
        }

        public bool TryGetSearchParameter(string resourceType, string code, bool excludePendingDelete, out SearchParameterInfo searchParameter)
        {
            EnsureInitialized();
            searchParameter = null;

            if (TypeLookup.TryGetValue(resourceType, out ConcurrentDictionary<string, SearchParameterInfo> searchParameters) &&
                searchParameters.TryGetValue(code, out searchParameter))
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
            foreach (var resourceType in allResourceTypes)
            {
                TypeLookup[resourceType].TryRemove(searchParameterInfo.Code, out var removedParam);
                if (removedParam != null && removedParam.Url != searchParameterInfo.Url)
                {
                    _logger.LogError("Error, Search Param {RemovedParam} removed from Search Param Definition manager.  It does not match deleted Search Param {Url}", removedParam.Url, url);
                }
            }

            if (calculateHash)
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
            string continuationToken = null;
            do
            {
                var searchOptions = new SearchOptions();
                searchOptions.Sort = new List<(SearchParameterInfo, SortOrder)>();
                searchOptions.UnsupportedSearchParams = new List<Tuple<string, string>>();
                searchOptions.Expression = Expression.SearchParameter(SearchParameterInfo.ResourceTypeSearchParameter, Expression.StringEquals(FieldName.TokenCode, null, KnownResourceTypes.SearchParameter, false));
                searchOptions.MaxItemCount = 10;
                if (continuationToken != null)
                {
                    searchOptions.ContinuationToken = continuationToken;
                }

                var result = await search.Value.SearchAsync(searchOptions, cancellationToken);
                continuationToken = result?.ContinuationToken;

                if (result?.Results != null && result.Results.Any())
                {
                    var searchParams = result.Results.Select(r => r.Resource.RawResource.ToITypedElement(_modelInfoProvider)).ToList();

                    _logger.LogInformation("There are {CustomSearchParameters} custom Search Parameters", result.Results.Count().ToString());

                    foreach (var searchParam in searchParams)
                    {
                        try
                        {
                            SearchParameterDefinitionBuilder.Build(
                                new List<ITypedElement>() { searchParam },
                                UrlLookup,
                                TypeLookup,
                                _modelInfoProvider,
                                _logger);
                        }
                        catch (FhirException ex)
                        {
                            StringBuilder issueDetails = new StringBuilder();
                            foreach (OperationOutcomeIssue issue in ex.Issues)
                            {
                                issueDetails.Append(issue.Diagnostics).Append("; ");
                            }

                            _logger.LogWarning(ex, "Error loading search parameter {Url} from data store. Issues: {Issues}", searchParam.GetStringScalar("url"), issueDetails.ToString());
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error loading search parameter {Url} from data store.", searchParam.GetStringScalar("url"));
                        }
                    }
                }
            }
            while (continuationToken != null);
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
