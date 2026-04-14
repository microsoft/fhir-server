// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DotLiquid.Tags.Html;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Microsoft.Extensions.Logging;
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
            ILogger<SearchParameterOperations> logger)
        {
            EnsureArg.IsNotNull(searchParameterStatusManager, nameof(searchParameterStatusManager));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            EnsureArg.IsNotNull(searchParameterSupportResolver, nameof(searchParameterSupportResolver));
            EnsureArg.IsNotNull(dataStoreSearchParameterValidator, nameof(dataStoreSearchParameterValidator));
            EnsureArg.IsNotNull(fhirOperationDataStoreFactory, nameof(fhirOperationDataStoreFactory));
            EnsureArg.IsNotNull(searchServiceFactory, nameof(searchServiceFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _searchParameterStatusManager = searchParameterStatusManager;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _modelInfoProvider = modelInfoProvider;
            _searchParameterSupportResolver = searchParameterSupportResolver;
            _dataStoreSearchParameterValidator = dataStoreSearchParameterValidator;
            _fhirOperationDataStoreFactory = fhirOperationDataStoreFactory;
            _searchServiceFactory = searchServiceFactory;
            _logger = logger;
            _refreshSemaphore = new SemaphoreSlim(1, 1);
        }

        public DateTimeOffset? SearchParamLastUpdated => _searchParamLastUpdated;

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
                throw new JobConflictException(Core.Resources.ChangesToSearchParametersNotAllowedWhileReindexing);
            }
        }

        public async Task ValidateSearchParameterAsync(ITypedElement searchParam, CancellationToken cancellationToken)
        {
            var searchParameterWrapper = new SearchParameterWrapper(searchParam);
            var searchParameterUrl = searchParameterWrapper.Url;

            await SearchParameterConcurrencyManager.ExecuteWithLockAsync(
                searchParameterUrl,
                async () =>
                {
                    try
                    {
                        // We need to make sure we have the latest search parameters before trying to add
                        // a search parameter. This is to avoid creating a duplicate search parameter that
                        // was recently added and that hasn't propogated to all fhir-server instances.
                        await GetAndApplySearchParameterUpdates(cancellationToken);

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
                },
                _logger,
                cancellationToken);
        }

        /// <summary>
        /// Marks the Search Parameter as PendingDelete. This is only used by DeletionService.cs and will be removed when refactoring is done
        /// to allow deletion service to properly handle Hard deletions for Search Parameters (e.g. allow reindex prior to removing resource from DB).
        /// </summary>
        /// <param name="searchParamResource">Search Parameter to update to Pending Delete status.</param>
        /// <param name="cancellationToken">Cancellation Token</param>
        /// <param name="ignoreSearchParameterNotSupportedException">The value indicating whether to ignore SearchParameterNotSupportedException.</param>
        public async Task DeleteSearchParameterAsync(RawResource searchParamResource, CancellationToken cancellationToken, bool ignoreSearchParameterNotSupportedException = false)
        {
            var searchParam = _modelInfoProvider.ToTypedElement(searchParamResource);
            var searchParameterUrl = searchParam.GetStringScalar("url");

            await SearchParameterConcurrencyManager.ExecuteWithLockAsync(
                searchParameterUrl,
                async () =>
                {
                    try
                    {
                        await EnsureNoActiveReindexJobAsync(cancellationToken);

                        _logger.LogInformation("Deleting the search parameter '{Url}'", searchParameterUrl);
                        await _searchParameterStatusManager.UpdateSearchParameterStatusAsync(new[] { searchParameterUrl }, SearchParameterStatus.PendingDelete, cancellationToken);
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
                },
                _logger,
                cancellationToken);
        }

        public async Task UpdateSearchParameterStatusAsync(IReadOnlyCollection<string> searchParameterUris, SearchParameterStatus status, CancellationToken cancellationToken, bool ignoreSearchParameterNotSupportedException = false)
        {
            await EnsureNoActiveReindexJobAsync(cancellationToken);
            await _searchParameterStatusManager.UpdateSearchParameterStatusAsync(searchParameterUris, status, cancellationToken, ignoreSearchParameterNotSupportedException);
        }

        /// <summary>
        /// This method should be called periodically to get any updates to SearchParameters
        /// added to the DB by other service instances.
        /// It should also be called when a user starts a reindex job
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

                foreach (var searchParam in statuses.Where(p => p.Status == SearchParameterStatus.PendingDelete))
                {
                    _searchParameterDefinitionManager.UpdateSearchParameterStatus(searchParam.Uri.OriginalString, SearchParameterStatus.PendingDelete);
                }

                // Identify all System Defined Search Parameters and filter them from statuses
                var systemDefinedSearchParameterUris = new HashSet<string>(
                    _searchParameterDefinitionManager.AllSearchParameters
                        .Where(p => p.IsSystemDefined)
                        .Select(p => p.Url.OriginalString),
                    StringComparer.Ordinal);

                var statusesToFetch = statuses
                    .Where(p => p.Status == SearchParameterStatus.Enabled || p.Status == SearchParameterStatus.Supported)
                    .Where(p => !systemDefinedSearchParameterUris.Contains(p.Uri.OriginalString)).ToList();

                // Batch fetch all SearchParameter resources in one call
                var searchParamResources = await GetSearchParametersByUrls(
                    statusesToFetch
                        .Select(p => p.Uri.OriginalString)
                        .ToList(),
                    cancellationToken);

                var paramsToAdd = new List<ITypedElement>();
                var allHaveResources = true;
                foreach (var searchParam in statusesToFetch)
                {
                    if (!searchParamResources.TryGetValue(searchParam.Uri.OriginalString, out var searchParamResource))
                    {
                        _logger.LogInformation(
                            "Updated SearchParameter status found for SearchParameter: {Url}, but did not find any SearchParameter resources when querying for this url.",
                            searchParam.Uri);

                        if (searchParam.LastUpdated > DateTimeOffset.UtcNow.AddMinutes(-10)) // same as for in cache
                        {
                            allHaveResources = false;
                        }

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

                var inCache = ParametersAreInCache(statusesToFetch, cancellationToken);

                // If cache is updated directly and not from the database not all will have corresponding resources.
                // Do not advance or log the timestamp unless the cache contents are conclusive for this cycle.
                if (inCache && allHaveResources && results.LastUpdated.HasValue)
                {
                    _searchParamLastUpdated = results.LastUpdated.Value; // this should be the only place in the code to assign last updated
                }

                if (_searchParamLastUpdated.HasValue)
                {
                    // Log to EventLog for cross-instance convergence tracking (SQL only; Cosmos/File are no-ops).
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

        // This should handle racing condition between saving new parameter on one VM and refreshing cache on the other,
        // when refresh is invoked between saving status and saving resource.
        // This will not be needed when order of saves is reversed (resource first, then status)
        private bool ParametersAreInCache(IReadOnlyCollection<ResourceSearchParameterStatus> statuses, CancellationToken cancellationToken)
        {
            var inCache = true;
            foreach (var status in statuses)
            {
                _searchParameterDefinitionManager.TryGetSearchParameter(status.Uri.OriginalString, out var existingSearchParam);
                if (existingSearchParam == null)
                {
                    var msg = $"Did not find in cache uri={status.Uri.OriginalString} status={status.Status}";
                    _logger.LogInformation(msg);

                    // if the parameter was updated in the last 10 minutes it's possible we hit race condition
                    // where status was updated but resource is not yet saved, so we should not consider this as cache miss
                    if (status.LastUpdated > DateTimeOffset.UtcNow.AddMinutes(-10))
                    {
                        inCache = false;
                    }
                }
            }

            return inCache;
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

        private async Task<Dictionary<string, ITypedElement>> GetSearchParametersByUrls(List<string> urls, CancellationToken cancellationToken)
        {
            if (!urls.Any())
            {
                return new Dictionary<string, ITypedElement>();
            }

            const int chunkSize = 100;
            var searchParametersByUrl = new Dictionary<string, ITypedElement>(StringComparer.Ordinal);
            var unresolvedUrls = new HashSet<string>(urls, StringComparer.Ordinal);

            using IScoped<ISearchService> search = _searchServiceFactory.Invoke();

            string continuationToken = null;

            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                var queryParams = new List<Tuple<string, string>>
                {
                    Tuple.Create(KnownQueryParameterNames.Count, chunkSize.ToString()),
                };

                if (!string.IsNullOrEmpty(continuationToken))
                {
                    queryParams.Add(
                        Tuple.Create(
                            KnownQueryParameterNames.ContinuationToken,
                            ContinuationTokenEncoder.Encode(continuationToken)));
                }

                var result = await search.Value.SearchAsync(KnownResourceTypes.SearchParameter, queryParams, cancellationToken);
                if (result?.Results != null)
                {
                    foreach (var entry in result.Results)
                    {
                        var typedElement = entry.Resource?.RawResource?.ToITypedElement(_modelInfoProvider);
                        if (typedElement == null)
                        {
                            continue;
                        }

                        var url = typedElement.GetStringScalar("url");
                        if (!string.IsNullOrEmpty(url) && unresolvedUrls.Remove(url))
                        {
                            searchParametersByUrl[url] = typedElement;

                            if (unresolvedUrls.Count == 0)
                            {
                                return searchParametersByUrl;
                            }
                        }
                    }
                }

                continuationToken = result?.ContinuationToken;
            }
            while (!string.IsNullOrEmpty(continuationToken));

            if (unresolvedUrls.Count > 0)
            {
                _logger.LogWarning(
                    "Could not resolve {Count} SearchParameter URL(s). Samples: {Urls}",
                    unresolvedUrls.Count,
                    string.Join(", ", unresolvedUrls.Take(10)));
            }

            return searchParametersByUrl;
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
