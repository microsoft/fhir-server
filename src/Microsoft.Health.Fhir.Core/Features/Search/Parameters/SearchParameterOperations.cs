// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Rest;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Definition.BundleWrappers;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete.Messages;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Models;
using Polly;
using Polly.Retry;

namespace Microsoft.Health.Fhir.Core.Features.Search.Parameters
{
    public class SearchParameterOperations : ISearchParameterOperations, INotificationHandler<BulkDeleteMetricsNotification>
    {
        private const int DefaultDeleteTasksPerPage = 5;

        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly SearchParameterStatusManager _searchParameterStatusManager;
        private readonly IModelInfoProvider _modelInfoProvider;
        private readonly ISearchParameterSupportResolver _searchParameterSupportResolver;
        private readonly IDataStoreSearchParameterValidator _dataStoreSearchParameterValidator;
        private readonly Func<IScoped<ISearchService>> _searchServiceFactory;
        private readonly ILogger _logger;
        private readonly AsyncRetryPolicy _retryPolicy;

        public SearchParameterOperations(
            SearchParameterStatusManager searchParameterStatusManager,
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            IModelInfoProvider modelInfoProvider,
            ISearchParameterSupportResolver searchParameterSupportResolver,
            IDataStoreSearchParameterValidator dataStoreSearchParameterValidator,
            Func<IScoped<ISearchService>> searchServiceFactory,
            ILogger<SearchParameterOperations> logger)
        {
            EnsureArg.IsNotNull(searchParameterStatusManager, nameof(searchParameterStatusManager));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            EnsureArg.IsNotNull(searchParameterSupportResolver, nameof(searchParameterSupportResolver));
            EnsureArg.IsNotNull(dataStoreSearchParameterValidator, nameof(dataStoreSearchParameterValidator));
            EnsureArg.IsNotNull(searchServiceFactory, nameof(searchServiceFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _searchParameterStatusManager = searchParameterStatusManager;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _modelInfoProvider = modelInfoProvider;
            _searchParameterSupportResolver = searchParameterSupportResolver;
            _dataStoreSearchParameterValidator = dataStoreSearchParameterValidator;
            _searchServiceFactory = searchServiceFactory;
            _logger = logger;

            _retryPolicy = Policy
                .Handle<SearchParameterNotSupportedException>()
                .WaitAndRetryAsync(3, count => TimeSpan.FromSeconds(Math.Pow(2, count)));
        }

        public async Task AddSearchParameterAsync(ITypedElement searchParam, CancellationToken cancellationToken)
        {
            try
            {
                // verify the parameter is supported before continuing
                var searchParameterWrapper = new SearchParameterWrapper(searchParam);
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

                _logger.LogInformation("Adding the search parameter '{Url}'", searchParameterWrapper.Url);
                _searchParameterDefinitionManager.AddNewSearchParameters(new List<ITypedElement> { searchParam });

                await _searchParameterStatusManager.AddSearchParameterStatusAsync(new List<string> { searchParameterWrapper.Url }, cancellationToken);
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
                _logger.LogError(ex, "Error adding search parameter.");
                var customSearchException = new ConfigureCustomSearchException(Core.Resources.CustomSearchCreateError);
                customSearchException.Issues.Add(new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Error,
                    OperationOutcomeConstants.IssueType.Exception,
                    ex.Message));

                throw customSearchException;
            }
        }

        /// <summary>
        /// Marks the Search Parameter as PendingDelete.
        /// </summary>
        /// <param name="searchParamResource">Search Parameter to update to Pending Delete status.</param>
        /// <param name="cancellationToken">Cancellation Token</param>
        public async Task DeleteSearchParameterAsync(RawResource searchParamResource, CancellationToken cancellationToken)
        {
            try
            {
                // We need to make sure we have the latest search parameters before trying to delete
                // existing search parameter. This is to avoid trying to update a search parameter that
                // was recently added and that hasn't propogated to all fhir-server instances.
                await GetAndApplySearchParameterUpdates(cancellationToken);

                var searchParam = _modelInfoProvider.ToTypedElement(searchParamResource);
                var searchParameterUrl = searchParam.GetStringScalar("url");

                // First we delete the status metadata from the data store as this function depends on
                // the in memory definition manager.  Once complete we remove the SearchParameter from
                // the definition manager.
                _logger.LogInformation("Deleting the search parameter '{Url}'", searchParameterUrl);
                await _searchParameterStatusManager.UpdateSearchParameterStatusAsync(new List<string>() { searchParameterUrl }, SearchParameterStatus.PendingDelete, cancellationToken);

                // Update the status of the search parameter in the definition manager once the status is updated in the store.
                _searchParameterDefinitionManager.UpdateSearchParameterStatus(searchParameterUrl, SearchParameterStatus.PendingDelete);
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting search parameter.");
                var customSearchException = new ConfigureCustomSearchException(Core.Resources.CustomSearchDeleteError);
                customSearchException.Issues.Add(new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Error,
                    OperationOutcomeConstants.IssueType.Exception,
                    ex.Message));

                throw customSearchException;
            }
        }

        public async Task UpdateSearchParameterAsync(ITypedElement searchParam, RawResource previousSearchParam, CancellationToken cancellationToken)
        {
            try
            {
                // We need to make sure we have the latest search parameters before trying to update
                // existing search parameter. This is to avoid trying to update a search parameter that
                // was recently added and that hasn't propogated to all fhir-server instances.
                await GetAndApplySearchParameterUpdates(cancellationToken);

                var searchParameterWrapper = new SearchParameterWrapper(searchParam);
                var searchParameterInfo = new SearchParameterInfo(searchParameterWrapper);
                (bool Supported, bool IsPartiallySupported) supportedResult = _searchParameterSupportResolver.IsSearchParameterSupported(searchParameterInfo);

                if (!supportedResult.Supported)
                {
                    throw new SearchParameterNotSupportedException(searchParameterInfo.Url);
                }

                // check data store specific support for SearchParameter
                if (!_dataStoreSearchParameterValidator.ValidateSearchParameter(searchParameterInfo, out var errorMessage))
                {
                    throw new SearchParameterNotSupportedException(errorMessage);
                }

                var prevSearchParam = _modelInfoProvider.ToTypedElement(previousSearchParam);
                var prevSearchParamUrl = prevSearchParam.GetStringScalar("url");

                // As any part of the SearchParameter may have been changed, including the URL
                // the most reliable method of updating the SearchParameter is to delete the previous
                // data and insert the updated version
                _logger.LogInformation("Deleting the search parameter '{Url}' (update step 1/2)", prevSearchParamUrl);
                await _searchParameterStatusManager.DeleteSearchParameterStatusAsync(prevSearchParamUrl, cancellationToken);
                _searchParameterDefinitionManager.DeleteSearchParameter(prevSearchParam);

                _logger.LogInformation("Adding the search parameter '{Url}' (update step 2/2)", searchParameterWrapper.Url);
                _searchParameterDefinitionManager.AddNewSearchParameters(new List<ITypedElement>() { searchParam });
                await _searchParameterStatusManager.AddSearchParameterStatusAsync(new List<string>() { searchParameterWrapper.Url }, cancellationToken);
            }
            catch (FhirException fex)
            {
                _logger.LogError(fex, "Error updating search parameter.");
                fex.Issues.Add(new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Error,
                    OperationOutcomeConstants.IssueType.Exception,
                    Core.Resources.CustomSearchUpdateError));

                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating search parameter.");
                var customSearchException = new ConfigureCustomSearchException(Core.Resources.CustomSearchUpdateError);
                customSearchException.Issues.Add(new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Error,
                    OperationOutcomeConstants.IssueType.Exception,
                    ex.Message));

                throw customSearchException;
            }
        }

        /// <summary>
        /// This method should be called periodically to get any updates to SearchParameters
        /// added to the DB by other service instances.
        /// It should also be called when a user starts a reindex job
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>A task.</returns>
        public async Task GetAndApplySearchParameterUpdates(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation($"[Debug: {DateTime.UtcNow.ToString("s")}] Starting GetAndApplySearchParameterUpdates...");
            var updatedSearchParameterStatus = await _searchParameterStatusManager.GetSearchParameterStatusUpdates(cancellationToken);
            _logger.LogInformation($"[Debug: {DateTime.UtcNow.ToString("s")}] {updatedSearchParameterStatus.Count} search parameter status to update.");

            // First process any deletes or disables, then we will do any adds or updates
            // this way any deleted or params which might have the same code or name as a new
            // parameter will not cause conflicts. Disabled params just need to be removed when calculating the hash.
            foreach (var searchParam in updatedSearchParameterStatus.Where(p => p.Status == SearchParameterStatus.Deleted))
            {
                _logger.LogInformation($"[Debug: {DateTime.UtcNow.ToString("s")}] Deleting {searchParam.Uri} (Status: Deleted)...");
                DeleteSearchParameter(searchParam.Uri.OriginalString);
                _logger.LogInformation($"[Debug: {DateTime.UtcNow.ToString("s")}] Deleting {searchParam.Uri} (Status: Deleted) completed.");
            }

            foreach (var searchParam in updatedSearchParameterStatus.Where(p => p.Status == SearchParameterStatus.PendingDelete))
            {
                _logger.LogInformation($"[Debug: {DateTime.UtcNow.ToString("s")}] Updating {searchParam.Uri} to PendingDeleted...");
                _searchParameterDefinitionManager.UpdateSearchParameterStatus(searchParam.Uri.AbsolutePath, SearchParameterStatus.PendingDelete);
                _logger.LogInformation($"[Debug: {DateTime.UtcNow.ToString("s")}] Updating {searchParam.Uri} to PendingDeleted completed.");
            }

            var paramsToAdd = new List<ITypedElement>();
            foreach (var searchParam in updatedSearchParameterStatus.Where(p => p.Status == SearchParameterStatus.Enabled || p.Status == SearchParameterStatus.Supported))
            {
                var searchParamResource = await GetSearchParameterByUrl(searchParam.Uri.OriginalString, cancellationToken);

                if (searchParamResource == null)
                {
                    _logger.LogInformation(
                        "Updated SearchParameter status found for SearchParameter: {Url}, but did not find any SearchParameter resources when querying for this url.",
                        searchParam.Uri);
                    continue;
                }

                if (_searchParameterDefinitionManager.TryGetSearchParameter(searchParam.Uri.OriginalString, out var existingSearchParam))
                {
                    _logger.LogInformation($"[Debug: {DateTime.UtcNow.ToString("s")}] Deleting {searchParam.Uri} (Status: {searchParam.Status})...");

                    // if the previous version of the search parameter exists we should delete the old information currently stored
                    DeleteSearchParameter(searchParam.Uri.OriginalString);
                    _logger.LogInformation($"[Debug: {DateTime.UtcNow.ToString("s")}] Deleting {searchParam.Uri} (Status: {searchParam.Status}) completed.");
                }

                paramsToAdd.Add(searchParamResource);
            }

            // Now add the new or updated parameters to the SearchParameterDefinitionManager
            if (paramsToAdd.Any())
            {
                _logger.LogInformation($"[Debug: {DateTime.UtcNow.ToString("s")}] Adding {paramsToAdd.Count} search parameters to the definition manager...");
                _searchParameterDefinitionManager.AddNewSearchParameters(paramsToAdd);
                _logger.LogInformation($"[Debug: {DateTime.UtcNow.ToString("s")}] Adding {paramsToAdd.Count} search parameters to the definition manager completed.");
            }

            var updatedStatus = updatedSearchParameterStatus.Where(p => p.Status == SearchParameterStatus.Enabled || p.Status == SearchParameterStatus.Supported).ToList();
            _logger.LogInformation($"[Debug: {DateTime.UtcNow.ToString("s")}] Applying {updatedStatus.Count} search parameter status...");

            // Once added to the definition manager we can update their status
            await _searchParameterStatusManager.ApplySearchParameterStatus(
                updatedSearchParameterStatus.Where(p => p.Status == SearchParameterStatus.Enabled || p.Status == SearchParameterStatus.Supported).ToList(),
                cancellationToken);
            _logger.LogInformation($"[Debug: {DateTime.UtcNow.ToString("s")}] Applying {updatedStatus.Count} search parameter status completed.");

            _logger.LogInformation($"[Debug: {DateTime.UtcNow.ToString("s")}] GetAndApplySearchParameterUpdates completed:{Environment.NewLine}{string.Join(Environment.NewLine, updatedStatus.Select(x => x.Uri))}");
        }

        public async Task Handle(BulkDeleteMetricsNotification notification, CancellationToken cancellationToken)
        {
            _logger.LogInformation($"[Debug: {DateTime.UtcNow.ToString("s")}] Handling BulkDeleteMetricsNotification...");
            var content = notification?.Content?.ToString();
            if (!string.IsNullOrWhiteSpace(content))
            {
                try
                {
                    var urls = JsonSerializer.Deserialize<List<string>>(content);
                    _logger.LogInformation($"[Debug: {DateTime.UtcNow.ToString("s")}] Found {urls.Count} search parameters to delete...");

                    if (urls.Any())
                    {
                        await DeleteSearchParametersAsync(urls, DefaultDeleteTasksPerPage, cancellationToken);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Failed to deserialize the notification content.");
                    throw;
                }
            }

            _logger.LogInformation($"[Debug: {DateTime.UtcNow.ToString("s")}] Handling BulkDeleteMetricsNotification completed.");
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

        private async Task<ITypedElement> GetSearchParameterByUrl(string url, CancellationToken cancellationToken)
        {
            using IScoped<ISearchService> search = _searchServiceFactory.Invoke();
            var queryParams = new List<Tuple<string, string>>();

            queryParams.Add(new Tuple<string, string>("url", url));
            var result = await search.Value.SearchAsync(KnownResourceTypes.SearchParameter, queryParams, cancellationToken);

            if (result.Results.Any())
            {
                if (result.Results.Count() > 1)
                {
                    _logger.LogWarning("More than one SearchParameter found with url {Url}. This may cause unpredictable behavior.", url);
                }

                // There should be only one SearchParameter per url
                return result.Results.First().Resource.RawResource.ToITypedElement(_modelInfoProvider);
            }

            return null;
        }

        private Task DeleteSearchParametersAsync(List<string> urls, int pageSize, CancellationToken cancellationToken)
        {
            return _retryPolicy.ExecuteAsync(
                async () =>
                {
                    if (urls?.Any(x => x != null) ?? false)
                    {
                        _logger.LogInformation($"[Debug: {DateTime.UtcNow.ToString("s")}] Deleting {urls.Count} search parameters...");
                        await GetAndApplySearchParameterUpdates(cancellationToken);

                        var count = 0;
                        while (count < urls.Count)
                        {
                            var urlsToDelete = urls.Skip(count).Take(pageSize).ToList();
                            count += urlsToDelete.Count;

                            var tasks = new List<Task>();
                            foreach (var url in urlsToDelete)
                            {
                                tasks.Add(
                                    Task.Run(
                                        async () =>
                                        {
                                            try
                                            {
                                                // First we delete the status metadata from the data store as this function depends on
                                                // the in memory definition manager.  Once complete we remove the SearchParameter from
                                                // the definition manager.
                                                _logger.LogInformation("Deleting the search parameter '{Url}'", url);
                                                await _searchParameterStatusManager.UpdateSearchParameterStatusAsync(new List<string>() { url }, SearchParameterStatus.PendingDelete, cancellationToken);

                                                // Update the status of the search parameter in the definition manager once the status is updated in the store.
                                                _searchParameterDefinitionManager.UpdateSearchParameterStatus(url, SearchParameterStatus.PendingDelete);
                                            }
                                            catch (Exception ex)
                                            {
                                                _logger.LogError(ex, $"Failed to delete a search parameter with url: {url}");
                                                throw;
                                            }
                                        },
                                        cancellationToken));
                            }

                            try
                            {
                                if (tasks.Any())
                                {
                                    await Task.WhenAll(tasks);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, $"Failed to delete {tasks.Where(x => !x.IsCompleted).Count()} search parameters.");
                                throw;
                            }

                            _logger.LogInformation($"[Debug: {DateTime.UtcNow.ToString("s")}] Deleting {urls.Count} search parameters completed.");
                        }
                    }
                });
        }
    }
}
