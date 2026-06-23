// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Definition.BundleWrappers;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Models;
using Newtonsoft.Json.Linq;

namespace Microsoft.Health.Fhir.Core.Features.Search.Parameters
{
    public class SearchParameterOperations : ISearchParameterOperations, IDisposable
    {
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly SearchParameterStatusManager _searchParameterStatusManager;
        private readonly IModelInfoProvider _modelInfoProvider;
        private readonly ISearchParameterSupportResolver _searchParameterSupportResolver;
        private readonly IDataStoreSearchParameterValidator _dataStoreSearchParameterValidator;
        private readonly Func<IScoped<IFhirOperationDataStore>> _fhirOperationDataStoreFactory;
        private readonly Func<IScoped<ISearchService>> _searchServiceFactory;
        private readonly IScopeProvider<IFhirDataStore> _fhirDataStoreFactory;
        private readonly IResourceWrapperFactory _resourceWrapperFactory;
        private readonly ILogger _logger;
        private DateTimeOffset? _searchParamLastUpdated;
        private readonly SemaphoreSlim _refreshSemaphore;
        private readonly int maxSecondsToWait = 100;

        public SearchParameterOperations(
            SearchParameterStatusManager searchParameterStatusManager,
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            IModelInfoProvider modelInfoProvider,
            ISearchParameterSupportResolver searchParameterSupportResolver,
            IDataStoreSearchParameterValidator dataStoreSearchParameterValidator,
            Func<IScoped<IFhirOperationDataStore>> fhirOperationDataStoreFactory,
            Func<IScoped<ISearchService>> searchServiceFactory,
            IScopeProvider<IFhirDataStore> fhirDataStoreFactory,
            IResourceWrapperFactory resourceWrapperFactory,
            ILogger<SearchParameterOperations> logger)
        {
            EnsureArg.IsNotNull(searchParameterStatusManager, nameof(searchParameterStatusManager));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            EnsureArg.IsNotNull(searchParameterSupportResolver, nameof(searchParameterSupportResolver));
            EnsureArg.IsNotNull(dataStoreSearchParameterValidator, nameof(dataStoreSearchParameterValidator));
            EnsureArg.IsNotNull(fhirOperationDataStoreFactory, nameof(fhirOperationDataStoreFactory));
            EnsureArg.IsNotNull(searchServiceFactory, nameof(searchServiceFactory));
            EnsureArg.IsNotNull(fhirDataStoreFactory, nameof(fhirDataStoreFactory));
            EnsureArg.IsNotNull(resourceWrapperFactory, nameof(resourceWrapperFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _searchParameterStatusManager = searchParameterStatusManager;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _modelInfoProvider = modelInfoProvider;
            _searchParameterSupportResolver = searchParameterSupportResolver;
            _dataStoreSearchParameterValidator = dataStoreSearchParameterValidator;
            _fhirOperationDataStoreFactory = fhirOperationDataStoreFactory;
            _searchServiceFactory = searchServiceFactory;
            _fhirDataStoreFactory = fhirDataStoreFactory;
            _resourceWrapperFactory = resourceWrapperFactory;
            _logger = logger;
            _refreshSemaphore = new SemaphoreSlim(1, 1);
        }

        public DateTimeOffset SearchParamLastUpdated
        {
            get
            {
                if (!_searchParamLastUpdated.HasValue)
                {
                    throw new InvalidOperationException("Search param cache has not been updated yet.");
                }

                return _searchParamLastUpdated.Value;
            }
        }

        public string GetSearchParameterHash(string resourceType)
        {
            EnsureArg.IsNotNullOrWhiteSpace(resourceType, nameof(resourceType));

            if (_searchParameterDefinitionManager?.SearchParameterHashMap == null)
            {
                return null;
            }
            else
            {
                return _searchParameterDefinitionManager.SearchParameterHashMap.TryGetValue(resourceType, out string hash) ? hash : null;
            }
        }

        public async Task EnsureNoActiveReindexJobAsync(CancellationToken cancellationToken)
        {
            using IScoped<IFhirOperationDataStore> fhirOperationDataStore = _fhirOperationDataStoreFactory();
            (bool found, string id) activeReindexJob = await fhirOperationDataStore.Value.CheckActiveReindexJobsAsync(cancellationToken);

            if (activeReindexJob.found)
            {
                throw new JobConflictException(string.Format(Core.Resources.ChangesToSearchParametersNotAllowedWhileReindexing, activeReindexJob.id));
            }
        }

        public async Task<DateTimeOffset> ValidateSearchParameterAsync(ITypedElement searchParam, CancellationToken cancellationToken, DateTimeOffset? lastUpdated = null)
        {
            var searchParameterWrapper = new SearchParameterWrapper(searchParam);
            var searchParameterUrl = searchParameterWrapper.Url;
            try
            {
                // We need to make sure we have the latest search parameters before trying to add
                // a search parameter. This is to avoid creating a duplicate search parameter that
                // was recently added and that hasn't propogated to all fhir-server instances.
                // if last updated is provided, it means that updates were applied by pipeline. In this case do not update and keep the input.
                if (!lastUpdated.HasValue)
                {
                    await GetAndApplySearchParameterUpdates(cancellationToken);
                    lastUpdated = SearchParamLastUpdated;
                }

                // verify the parameter is supported before continuing
                var searchParameterInfo = new SearchParameterInfo(searchParameterWrapper);

                if (searchParameterInfo.Component?.Any() == true)
                {
                    foreach (SearchParameterComponentInfo c in searchParameterInfo.Component)
                    {
                        c.ResolvedSearchParameter = _searchParameterDefinitionManager.GetSearchParameter(c.DefinitionUrl.OriginalString);
                    }
                }

                (bool Supported, bool IsPartiallySupported) supportedResult = _searchParameterSupportResolver.IsSearchParameterSupported(searchParameterInfo);

                if (!supportedResult.Supported)
                {
                    throw new SearchParameterNotSupportedException(string.Format(Core.Resources.NoConverterForSearchParamType, searchParameterInfo.Type, searchParameterInfo.Expression));
                }

                // check data store specific support for SearchParameter
                if (!_dataStoreSearchParameterValidator.ValidateSearchParameter(searchParameterInfo, out var errorMessage))
                {
                    throw new SearchParameterNotSupportedException(errorMessage);
                }
            }
            catch (FhirException fex)
            {
                _logger.LogError(fex, "Error adding search parameter.");
                fex.Issues.Add(new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Error,
                    OperationOutcomeConstants.IssueType.Exception,
                    Core.Resources.CustomSearchCreateError));

                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error adding search parameter.");
                var customSearchException = new ConfigureCustomSearchException(Core.Resources.CustomSearchCreateError);
                customSearchException.Issues.Add(new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Error,
                    OperationOutcomeConstants.IssueType.Exception,
                    ex.Message));

                throw customSearchException;
            }

            return lastUpdated.Value;
        }

        /// <summary>
        /// Marks the Search Parameter as PendingDelete or PendingHardDelete. This is only used by DeletionService.cs and will be removed when refactoring is done
        /// to allow deletion service to properly handle Hard deletions for Search Parameters (e.g. allow reindex prior to removing resource from DB).
        /// !!! This method has incorrect name. It does not delete search parameter, it just updates its status.
        /// </summary>
        /// <param name="searchParamResource">Search Parameter to update to Pending Delete status.</param>
        /// <param name="cancellationToken">Cancellation Token</param>
        /// <param name="ignoreSearchParameterNotSupportedException">The value indicating whether to ignore SearchParameterNotSupportedException.</param>
        /// <param name="isHardDelete">True for hard delete (PendingHardDelete), false for soft delete (PendingDelete).</param>
        public async Task DeleteSearchParameterAsync(RawResource searchParamResource, CancellationToken cancellationToken, bool ignoreSearchParameterNotSupportedException = false, bool isHardDelete = false)
        {
            var searchParam = _modelInfoProvider.ToTypedElement(searchParamResource);
            var searchParameterUrl = searchParam.GetStringScalar("url");

            try
            {
                await EnsureNoActiveReindexJobAsync(cancellationToken);

                _logger.LogInformation("DeleteSearchParameterAsync: Refreshing cache");
                await GetAndApplySearchParameterUpdates(cancellationToken);
                var status = isHardDelete ? SearchParameterStatus.PendingHardDelete : SearchParameterStatus.PendingDelete;
                _logger.LogInformation("DeleteSearchParameterAsync: Deleting the search parameter '{Url}' with status {Status}", searchParameterUrl, status);
                await _searchParameterStatusManager.UpdateSearchParameterStatusAsync(new[] { searchParameterUrl }, status, cancellationToken, lastUpdated: SearchParamLastUpdated);
            }
            catch (FhirException fex)
            {
                _logger.LogError(fex, "Error deleting search parameter.");
                fex.Issues.Add(new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Error,
                    OperationOutcomeConstants.IssueType.Exception,
                    Core.Resources.CustomSearchDeleteError));

                throw;
            }
            catch (Exception ex) when (!(ex is FhirException))
            {
                _logger.LogError(ex, "Unexpected error deleting search parameter.");
                var customSearchException = new ConfigureCustomSearchException(Core.Resources.CustomSearchDeleteError);
                customSearchException.Issues.Add(new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Error,
                    OperationOutcomeConstants.IssueType.Exception,
                    ex.Message));

                throw customSearchException;
            }
        }

        public async Task UpdateSearchParameterStatusAsync(IReadOnlyCollection<string> searchParameterUris, SearchParameterStatus status, CancellationToken cancellationToken, bool ignoreSearchParameterNotSupportedException = false)
        {
            await EnsureNoActiveReindexJobAsync(cancellationToken);
            await _searchParameterStatusManager.UpdateSearchParameterStatusAsync(searchParameterUris, status, cancellationToken, ignoreSearchParameterNotSupportedException);
        }

        /// <summary>
        /// This method should be called to get any updates to search param cache
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <param name="zeroWaitForSemaphore">Whether to wait for the semaphore to become available.</param>
        /// <returns>A task that returns true if the refresh was performed, false if it was skipped due to exceeding the lock interval.</returns>
        public async Task<bool> GetAndApplySearchParameterUpdates(CancellationToken cancellationToken = default, bool zeroWaitForSemaphore = false)
        {
            if (!await _refreshSemaphore.WaitAsync(TimeSpan.FromSeconds(zeroWaitForSemaphore ? 0 : maxSecondsToWait), cancellationToken))
            {
                var msg = $"Could not acquire lock to refresh search parameter cache after waiting for {maxSecondsToWait} seconds.";
                if (zeroWaitForSemaphore)
                {
                    _logger.LogInformation(msg);
                    return false;
                }
                else
                {
                    _logger.LogError(msg);
                    throw new InvalidOperationException(msg);
                }
            }

            try
            {
                var results = await _searchParameterStatusManager.GetSearchParameterStatusUpdates(cancellationToken, _searchParamLastUpdated);
                var statuses = results.Statuses;

                // First process any deletes or disables, then we will do any adds or updates
                // this way any deleted or params which might have the same code or name as a new
                // parameter will not cause conflicts. Disabled params just need to be removed when calculating the hash.
                foreach (var searchParam in statuses.Where(p => p.Status == SearchParameterStatus.Deleted))
                {
                    DeleteSearchParameter(searchParam.Uri.OriginalString);
                }

                foreach (var searchParam in statuses.Where(_ => _.Status == SearchParameterStatus.PendingDelete || _.Status == SearchParameterStatus.PendingHardDelete))
                {
                    _searchParameterDefinitionManager.UpdateSearchParameterStatus(searchParam.Uri.OriginalString, searchParam.Status);
                }

                // Identify all System Defined Search Parameters and filter them from statuses
                var systemDefinedSearchParameterUris = new HashSet<string>(_searchParameterDefinitionManager.AllSearchParameters.Where(p => p.IsSystemDefined).Select(p => p.Url.OriginalString));

                var statusesToFetch = statuses
                                        .Where(p => p.Status == SearchParameterStatus.Enabled || p.Status == SearchParameterStatus.Supported)
                                        .Where(p => !systemDefinedSearchParameterUris.Contains(p.Uri.OriginalString)).ToList();

                // Batch fetch all SearchParameter resources in one call
                var searchParamResources = await GetSearchParametersByUrlsAsync(statusesToFetch.Select(p => p.Uri.OriginalString).ToList(), cancellationToken);

                var paramsToAdd = new List<ITypedElement>();
                foreach (var searchParam in statusesToFetch)
                {
                    if (!searchParamResources.TryGetValue(searchParam.Uri.OriginalString, out var searchParamResource))
                    {
                        _logger.LogInformation(
                            "Updated SearchParameter status found for SearchParameter: {Url}, but did not find any SearchParameter resources when querying for this url.",
                            searchParam.Uri);
                        continue;
                    }

                    // check if search param is in cache and add if does not exist
                    if (_searchParameterDefinitionManager.TryGetSearchParameter(searchParam.Uri.OriginalString, out var existingSearchParam))
                    {
                        // if the previous version of the search parameter exists we should delete the old information currently stored
                        DeleteSearchParameter(searchParam.Uri.OriginalString);
                    }

                    paramsToAdd.Add(searchParamResource);

                    // Add parameters incrementally per chunk to reduce peak memory footprint
                    if (paramsToAdd.Count >= 100)
                    {
                        _searchParameterDefinitionManager.AddNewSearchParameters(paramsToAdd);
                        paramsToAdd.Clear();
                    }
                }

                // Add any remaining parameters
                if (paramsToAdd.Any())
                {
                    _searchParameterDefinitionManager.AddNewSearchParameters(paramsToAdd);
                }

                // Once added to the definition manager we can update their status
                await _searchParameterStatusManager.ApplySearchParameterStatus(statuses, cancellationToken);

                if (results.LastUpdated.HasValue)
                {
                    _searchParamLastUpdated = results.LastUpdated.Value; // this should be the only place in the code to assign last updated
                }

                if (zeroWaitForSemaphore && _searchParamLastUpdated.HasValue) // log only for background
                {
                    // log for cross-instance cache refresh tracking (SQL only; Cosmos/File are no-ops).
                    var lastUpdatedText = _searchParamLastUpdated.Value.ToString("yyyy-MM-dd HH:mm:ss.fffffff");
                    await _searchParameterStatusManager.TryLogEvent(_searchParameterStatusManager.SearchParamCacheUpdateProcessName, "Warn", lastUpdatedText, null, cancellationToken);
                }
            }
            finally
            {
                try
                {
                    _refreshSemaphore.Release();
                }
                catch (ObjectDisposedException)
                {
                    // Expected during host shutdown when Dispose() races with an in-flight async callback.
                }
            }

            return true;
        }

        private void DeleteSearchParameter(string url)
        {
            try
            {
                _searchParameterDefinitionManager.DeleteSearchParameter(url, false);
            }
            catch (ResourceNotFoundException)
            {
                // do nothing, there may not be a search parameter to remove
            }
        }

        public async Task DeleteSearchParameterResourceAsync(string searchParameterUrl, bool hardDelete, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrWhiteSpace(searchParameterUrl, nameof(searchParameterUrl));

            _logger.LogInformation("DeleteSearchParameterResourceAsync: Looking up resource for URL '{Url}'", searchParameterUrl);

            // Search for the resource by URL to get the typed element
            var results = await GetSearchParametersByUrlsAsync(new List<string> { searchParameterUrl }, cancellationToken);

            if (!results.TryGetValue(searchParameterUrl, out var typedElement))
            {
                _logger.LogInformation("DeleteSearchParameterResourceAsync: Search parameter resource with URL '{Url}' not found in data store. It may have already been deleted.", searchParameterUrl);
                return;
            }

            // Extract the resource ID from the typed element
            var resourceId = typedElement.GetStringScalar("id");
            if (string.IsNullOrEmpty(resourceId))
            {
                _logger.LogWarning("DeleteSearchParameterResourceAsync: Search parameter with URL '{Url}' found but has no ID.", searchParameterUrl);
                return;
            }

            var resourceKey = new ResourceKey(KnownResourceTypes.SearchParameter, resourceId);
            _logger.LogInformation("DeleteSearchParameterResourceAsync: {DeleteType} deleting search parameter resource '{ResourceId}' with URL '{Url}'", hardDelete ? "Hard" : "Soft", resourceId, searchParameterUrl);

            using var fhirDataStore = _fhirDataStoreFactory.Invoke();

            if (hardDelete)
            {
                await fhirDataStore.Value.HardDeleteAsync(resourceKey, keepCurrentVersion: false, allowPartialSuccess: false, cancellationToken);
            }
            else
            {
                await fhirDataStore.Value.UpsertAsync(
                    CreateDeleteResourceWrapperOperation(resourceKey),
                    cancellationToken);
            }

            _logger.LogInformation("DeleteSearchParameterResourceAsync: Successfully deleted search parameter resource '{ResourceId}' with URL '{Url}'", resourceKey.Id, searchParameterUrl);
        }

        private ResourceWrapperOperation CreateDeleteResourceWrapperOperation(ResourceKey resourceKey)
        {
            var json = new JObject
            {
                ["resourceType"] = resourceKey.ResourceType,
                ["id"] = resourceKey.Id,
                ["meta"] = new JObject { ["lastUpdated"] = Clock.UtcNow.UtcDateTime },
            };

            var rawResource = new RawResource(json.ToString(Newtonsoft.Json.Formatting.None), FhirResourceFormat.Json, isMetaSet: false);
            var typedElement = rawResource.ToITypedElement(_modelInfoProvider);
            var resourceElement = typedElement.ToResourceElement();

            var wrapper = _resourceWrapperFactory.Create(resourceElement, deleted: true, keepMeta: false, keepVersion: false);

            return new ResourceWrapperOperation(
                wrapper,
                allowCreate: true,
                keepHistory: true,
                weakETag: null,
                requireETagOnUpdate: false,
                keepVersion: false,
                bundleResourceContext: null);
        }

        public async Task<Dictionary<string, ITypedElement>> GetSearchParametersByUrlsAsync(IReadOnlyCollection<string> searchParameterUrls, CancellationToken cancellationToken)
        {
            var result = new Dictionary<string, ITypedElement>();
            var unresolvedUrls = new HashSet<string>(searchParameterUrls);

            // First, try direct search by URL in batches
            using var search = _searchServiceFactory.Invoke();
            const int chunkSize = 100;

            for (var i = 0; i < searchParameterUrls.Count; i += chunkSize)
            {
                var urlParam = string.Join(",", searchParameterUrls.Skip(i).Take(chunkSize));
                var queryParams = new List<Tuple<string, string>>
                {
                    Tuple.Create("url", urlParam),
                    Tuple.Create(KnownQueryParameterNames.Count, chunkSize.ToString()),
                };

                var searchResult = await search.Value.SearchAsync(KnownResourceTypes.SearchParameter, queryParams, cancellationToken);
                if (searchResult?.Results != null)
                {
                    foreach (var entry in searchResult.Results)
                    {
                        var typedElement = entry.Resource?.RawResource?.ToITypedElement(_modelInfoProvider);
                        if (typedElement != null)
                        {
                            var url = typedElement.GetStringScalar("url");
                            if (!string.IsNullOrEmpty(url) && unresolvedUrls.Contains(url))
                            {
                                result[url] = typedElement;
                                unresolvedUrls.Remove(url);
                            }
                        }
                    }
                }
            }

            if (unresolvedUrls.Count > 0)
            {
                _logger.LogWarning("Could not resolve {Count} SearchParameter URL(s). Samples: {Urls}", unresolvedUrls.Count, string.Join(", ", unresolvedUrls.Take(10)));
            }

            return result;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _refreshSemaphore?.Dispose();
            }
        }
    }
}
