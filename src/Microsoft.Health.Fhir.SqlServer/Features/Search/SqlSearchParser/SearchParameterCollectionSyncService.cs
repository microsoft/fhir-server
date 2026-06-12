// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser
{
    /// <summary>
    /// Service that keeps the SearchParameterCollection in sync with search parameter updates
    /// from the SearchParameterStatusManager by listening to SearchParametersInitializedNotification
    /// and SearchParametersUpdatedNotification.
    /// </summary>
    public class SearchParameterCollectionSyncService :
        INotificationHandler<SearchParametersInitializedNotification>,
        INotificationHandler<SearchParametersUpdatedNotification>
    {
        private readonly SearchParameterCollection _searchParameterCollection;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly ISqlServerFhirModel _sqlServerFhirModel;
        private readonly ILogger<SearchParameterCollectionSyncService> _logger;
        private bool _isInitialized;

        public SearchParameterCollectionSyncService(
            SearchParameterCollection searchParameterCollection,
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            ISqlServerFhirModel sqlServerFhirModel,
            ILogger<SearchParameterCollectionSyncService> logger)
        {
            _searchParameterCollection = EnsureArg.IsNotNull(searchParameterCollection, nameof(searchParameterCollection));
            _searchParameterDefinitionManager = EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            _sqlServerFhirModel = EnsureArg.IsNotNull(sqlServerFhirModel, nameof(sqlServerFhirModel));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        public Task Handle(SearchParametersInitializedNotification notification, CancellationToken cancellationToken)
        {
            if (_isInitialized)
            {
                _logger.LogDebug("SearchParameterCollection already initialized, skipping.");
                return Task.CompletedTask;
            }

            _logger.LogInformation("Initializing SearchParameterCollection with all searchable parameters.");

            try
            {
                // Get all searchable search parameters from the definition manager
                var searchableParameters = _searchParameterDefinitionManager.AllSearchParameters
                    .Where(p => p.IsSearchable && p.IsSupported)
                    .ToList();

                _logger.LogInformation("Found {Count} searchable parameters to add to collection.", searchableParameters.Count);

                foreach (var searchParamInfo in searchableParameters)
                {
                    try
                    {
                        var searchParameter = ConvertToSearchParameter(searchParamInfo);
                        if (searchParameter != null)
                        {
                            _searchParameterCollection.Add(searchParameter);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "Failed to add search parameter '{Code}' (URL: {Url}) to collection during initialization.",
                            searchParamInfo.Code,
                            searchParamInfo.Url);
                    }
                }

                _isInitialized = true;
                _logger.LogInformation("SearchParameterCollection initialized with {Count} parameters.", _searchParameterCollection.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while initializing SearchParameterCollection.");
                throw;
            }

            return Task.CompletedTask;
        }

        public Task Handle(SearchParametersUpdatedNotification notification, CancellationToken cancellationToken)
        {
            if (!_isInitialized)
            {
                _logger.LogDebug("SearchParameterCollection not yet initialized, skipping update.");
                return Task.CompletedTask;
            }

            if (notification.SearchParameters == null || !notification.SearchParameters.Any())
            {
                _logger.LogDebug("No search parameters to update.");
                return Task.CompletedTask;
            }

            _logger.LogInformation("Updating SearchParameterCollection with {Count} parameter changes.", notification.SearchParameters.Count);

            try
            {
                foreach (var searchParamInfo in notification.SearchParameters)
                {
                    try
                    {
                        // Remove the parameter if it exists (we'll re-add if it's still searchable)
                        var existingParam = _searchParameterCollection.GetByCode(searchParamInfo.Code);

                        if (searchParamInfo.IsSearchable && searchParamInfo.IsSupported)
                        {
                            // Add or update the parameter
                            var searchParameter = ConvertToSearchParameter(searchParamInfo);
                            if (searchParameter != null)
                            {
                                _searchParameterCollection.Add(searchParameter);
                                _logger.LogDebug("Updated search parameter '{Code}' in collection.", searchParamInfo.Code);
                            }
                        }
                        else if (existingParam != null)
                        {
                            // Parameter is no longer searchable - we can't remove from dictionary but log it
                            _logger.LogInformation(
                                "Search parameter '{Code}' (URL: {Url}) is no longer searchable (IsSearchable={IsSearchable}, IsSupported={IsSupported}). Note: existing entries remain in collection.",
                                searchParamInfo.Code,
                                searchParamInfo.Url,
                                searchParamInfo.IsSearchable,
                                searchParamInfo.IsSupported);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "Failed to update search parameter '{Code}' (URL: {Url}) in collection.",
                            searchParamInfo.Code,
                            searchParamInfo.Url);
                    }
                }

                _logger.LogInformation("SearchParameterCollection updated. Total count: {Count}", _searchParameterCollection.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while updating SearchParameterCollection.");
                throw;
            }

            return Task.CompletedTask;
        }

        private SearchParameter? ConvertToSearchParameter(Core.Models.SearchParameterInfo searchParamInfo)
        {
            try
            {
                // Get the SQL search parameter ID using the URL
                var searchParamId = _sqlServerFhirModel.GetSearchParamId(searchParamInfo.Url);

                // Map SearchParamType to the type string expected by the SQL parser
                var typeString = MapSearchParamType(searchParamInfo.Type);

                if (string.IsNullOrEmpty(typeString))
                {
                    _logger.LogDebug(
                        "Skipping search parameter '{Code}' with unsupported type '{Type}'.",
                        searchParamInfo.Code,
                        searchParamInfo.Type);
                    return null;
                }

                return new SearchParameter
                {
                    Code = searchParamInfo.Code,
                    Type = typeString,
                    Id = searchParamId,
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to convert SearchParameterInfo '{Code}' (URL: {Url}) to SearchParameter.",
                    searchParamInfo.Code,
                    searchParamInfo.Url);
                return null;
            }
        }

        private static string? MapSearchParamType(SearchParamType searchParamType)
        {
            return searchParamType switch
            {
                SearchParamType.Number => "NumberSearchParam",
                SearchParamType.Date => "DateTimeSearchParam",
                SearchParamType.String => "StringSearchParam",
                SearchParamType.Token => "TokenSearchParam",
                SearchParamType.Reference => "ReferenceSearchParam",
                SearchParamType.Composite => "CompositeSearchParam",
                SearchParamType.Quantity => "QuantitySearchParam",
                SearchParamType.Uri => "UriSearchParam",
                SearchParamType.Special => null, // Special search parameters are typically not stored in tables
                _ => null,
            };
        }
    }
}
