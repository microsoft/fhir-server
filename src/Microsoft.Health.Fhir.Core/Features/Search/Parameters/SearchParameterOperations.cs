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
    public class SearchParameterOperations : ISearchParameterOperations
    {
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly SearchParameterStatusManager _searchParameterStatusManager;
        private readonly IModelInfoProvider _modelInfoProvider;
        private readonly ISearchParameterSupportResolver _searchParameterSupportResolver;
        private readonly IDataStoreSearchParameterValidator _dataStoreSearchParameterValidator;
        private readonly Func<IScoped<IFhirOperationDataStore>> _fhirOperationDataStoreFactory;
        private readonly Func<IScoped<ISearchService>> _searchServiceFactory;
        private readonly ILogger _logger;

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
                        await _searchParameterDefinitionManager.GetAndApplySearchParameterUpdates(cancellationToken);

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
    }
}
