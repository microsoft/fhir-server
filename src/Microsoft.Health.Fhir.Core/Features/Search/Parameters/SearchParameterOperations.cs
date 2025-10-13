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
using Hl7.Fhir.ElementModel;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Definition.BundleWrappers;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Parameters
{
    public class SearchParameterOperations : ISearchParameterOperations
    {
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly SearchParameterStatusManager _searchParameterStatusManager;
        private readonly IModelInfoProvider _modelInfoProvider;
        private readonly ISearchParameterSupportResolver _searchParameterSupportResolver;
        private readonly IDataStoreSearchParameterValidator _dataStoreSearchParameterValidator;
        private readonly Func<IScoped<ISearchService>> _searchServiceFactory;
        private readonly ILogger _logger;

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
        }

        public string GetResourceTypeSearchParameterHashMap(string resourceType)
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

        public async Task AddSearchParameterAsync(ITypedElement searchParam, CancellationToken cancellationToken)
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
        /// Marks the Search Parameter as PendingDelete.
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
                        // We need to make sure we have the latest search parameters before trying to delete
                        // existing search parameter. This is to avoid trying to update a search parameter that
                        // was recently added and that hasn't propogated to all fhir-server instances.
                        await GetAndApplySearchParameterUpdates(cancellationToken);

                        // First we delete the status metadata from the data store as this function depends on
                        // the in memory definition manager.  Once complete we remove the SearchParameter from
                        // the definition manager.
                        _logger.LogInformation("Deleting the search parameter '{Url}'", searchParameterUrl);
                        await _searchParameterStatusManager.UpdateSearchParameterStatusAsync(new List<string>() { searchParameterUrl }, SearchParameterStatus.PendingDelete, cancellationToken, ignoreSearchParameterNotSupportedException);

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

        public async Task UpdateSearchParameterAsync(ITypedElement searchParam, RawResource previousSearchParam, CancellationToken cancellationToken)
        {
            var prevSearchParam = _modelInfoProvider.ToTypedElement(previousSearchParam);
            var prevSearchParamUrl = prevSearchParam.GetStringScalar("url");

            await SearchParameterConcurrencyManager.ExecuteWithLockAsync(
                prevSearchParamUrl,
                async () =>
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

                        // As any part of the SearchParameter may have been changed, including the URL
                        // the most reliable method of updating the SearchParameter is to delete the previous
                        // data and insert the updated version

                        if (!searchParameterWrapper.Url.Equals(prevSearchParamUrl, StringComparison.Ordinal))
                        {
                            _logger.LogInformation("Deleting the search parameter '{Url}' (update step 1/2)", prevSearchParamUrl);
                            await _searchParameterStatusManager.DeleteSearchParameterStatusAsync(prevSearchParamUrl, cancellationToken);
                            try
                            {
                                _searchParameterDefinitionManager.DeleteSearchParameter(prevSearchParam);
                            }
                            catch (ResourceNotFoundException)
                            {
                                // do nothing, there may not be a search parameter to remove
                            }
                        }

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
                    catch (Exception ex) when (!(ex is FhirException))
                    {
                        _logger.LogError(ex, "Unexpected error updating search parameter.");
                        var customSearchException = new ConfigureCustomSearchException(Core.Resources.CustomSearchUpdateError);
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
        /// This method should be called periodically to get any updates to SearchParameters
        /// added to the DB by other service instances.
        /// It should also be called when a user starts a reindex job
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>A task.</returns>
        public async Task GetAndApplySearchParameterUpdates(CancellationToken cancellationToken = default)
        {
            var updatedSearchParameterStatus = await _searchParameterStatusManager.GetSearchParameterStatusUpdates(cancellationToken);

            // First process any deletes or disables, then we will do any adds or updates
            // this way any deleted or params which might have the same code or name as a new
            // parameter will not cause conflicts. Disabled params just need to be removed when calculating the hash.
            foreach (var searchParam in updatedSearchParameterStatus.Where(p => p.Status == SearchParameterStatus.Deleted))
            {
                DeleteSearchParameter(searchParam.Uri.OriginalString);
            }

            foreach (var searchParam in updatedSearchParameterStatus.Where(p => p.Status == SearchParameterStatus.PendingDelete))
            {
                _searchParameterDefinitionManager.UpdateSearchParameterStatus(searchParam.Uri.OriginalString, SearchParameterStatus.PendingDelete);
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
                    // if the previous version of the search parameter exists we should delete the old information currently stored
                    DeleteSearchParameter(searchParam.Uri.OriginalString);
                }

                paramsToAdd.Add(searchParamResource);
            }

            // Now add the new or updated parameters to the SearchParameterDefinitionManager
            if (paramsToAdd.Any())
            {
                _searchParameterDefinitionManager.AddNewSearchParameters(paramsToAdd);
            }

            // Once added to the definition manager we can update their status

            await _searchParameterStatusManager.ApplySearchParameterStatus(
                updatedSearchParameterStatus,
                cancellationToken);
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
    }
}
