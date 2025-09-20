// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Storage;
using SqlDataReader = Microsoft.Data.SqlClient.SqlDataReader;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.Registry
{
    internal class SqlServerSearchParameterStatusDataStore : ISearchParameterStatusDataStore
    {
        private readonly IScopeProvider<SqlConnectionWrapperFactory> _scopedSqlConnectionWrapperFactory;
        private readonly VLatest.UpsertSearchParamsTvpGenerator<List<ResourceSearchParameterStatus>> _updateSearchParamsTvpGenerator;
        private readonly VLatest.UpsertSearchParamsWithOptimisticConcurrencyTvpGenerator<List<ResourceSearchParameterStatus>> _updateSearchParamsWithOptimisticConcurrencyTvpGenerator;
        private readonly ISearchParameterStatusDataStore _filebasedSearchParameterStatusDataStore;
        private readonly SchemaInformation _schemaInformation;
        private readonly SqlServerSortingValidator _sortingValidator;
        private readonly ISqlServerFhirModel _fhirModel;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly ILogger<SqlServerSearchParameterStatusDataStore> _logger;

        public SqlServerSearchParameterStatusDataStore(
            IScopeProvider<SqlConnectionWrapperFactory> scopedSqlConnectionWrapperFactory,
            VLatest.UpsertSearchParamsTvpGenerator<List<ResourceSearchParameterStatus>> updateSearchParamsTvpGenerator,
            VLatest.UpsertSearchParamsWithOptimisticConcurrencyTvpGenerator<List<ResourceSearchParameterStatus>> updateSearchParamsWithOptimisticConcurrencyTvpGenerator,
            FilebasedSearchParameterStatusDataStore.Resolver filebasedRegistry,
            SchemaInformation schemaInformation,
            SqlServerSortingValidator sortingValidator,
            ISqlServerFhirModel fhirModel,
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            ILogger<SqlServerSearchParameterStatusDataStore> logger)
        {
            EnsureArg.IsNotNull(scopedSqlConnectionWrapperFactory, nameof(scopedSqlConnectionWrapperFactory));
            EnsureArg.IsNotNull(updateSearchParamsTvpGenerator, nameof(updateSearchParamsTvpGenerator));
            EnsureArg.IsNotNull(filebasedRegistry, nameof(filebasedRegistry));
            EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
            EnsureArg.IsNotNull(sortingValidator, nameof(sortingValidator));
            EnsureArg.IsNotNull(fhirModel, nameof(fhirModel));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _scopedSqlConnectionWrapperFactory = scopedSqlConnectionWrapperFactory;
            _updateSearchParamsTvpGenerator = updateSearchParamsTvpGenerator;
            _updateSearchParamsWithOptimisticConcurrencyTvpGenerator = updateSearchParamsWithOptimisticConcurrencyTvpGenerator;
            _filebasedSearchParameterStatusDataStore = filebasedRegistry.Invoke();
            _schemaInformation = schemaInformation;
            _sortingValidator = sortingValidator;
            _fhirModel = fhirModel;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _logger = logger;
        }

        public async Task<IReadOnlyCollection<ResourceSearchParameterStatus>> GetSearchParameterStatuses(CancellationToken cancellationToken)
        {
            // If the search parameter table in SQL does not yet contain status columns
            if (_schemaInformation.Current < SchemaVersionConstants.SearchParameterStatusSchemaVersion)
            {
                // Get status information from file.
                return await _filebasedSearchParameterStatusDataStore.GetSearchParameterStatuses(cancellationToken);
            }

            using (IScoped<SqlConnectionWrapperFactory> scopedSqlConnectionWrapperFactory = _scopedSqlConnectionWrapperFactory.Invoke())
            using (SqlConnectionWrapper sqlConnectionWrapper = await scopedSqlConnectionWrapperFactory.Value.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                VLatest.GetSearchParamStatuses.PopulateCommand(sqlCommandWrapper);

                var parameterStatuses = new List<ResourceSearchParameterStatus>();

                using (SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken))
                {
                    while (await sqlDataReader.ReadAsync(cancellationToken))
                    {
                        short id;
                        string uri;
                        string stringStatus;
                        DateTimeOffset? lastUpdated;
                        bool? isPartiallySupported;

                        ResourceSearchParameterStatus resourceSearchParameterStatus;

                        (id, uri, stringStatus, lastUpdated, isPartiallySupported) = sqlDataReader.ReadRow(
                            VLatest.SearchParam.SearchParamId,
                            VLatest.SearchParam.Uri,
                            VLatest.SearchParam.Status,
                            VLatest.SearchParam.LastUpdated,
                            VLatest.SearchParam.IsPartiallySupported);

                        if (string.IsNullOrEmpty(stringStatus) || lastUpdated == null || isPartiallySupported == null)
                        {
                            // These columns are nullable because they are added to dbo.SearchParam in a later schema version.
                            // They should be populated as soon as they are added to the table and should never be null.
                            throw new SearchParameterNotSupportedException(Resources.SearchParameterStatusShouldNotBeNull);
                        }

                        var status = Enum.Parse<SearchParameterStatus>(stringStatus, true);

                        resourceSearchParameterStatus = new SqlServerResourceSearchParameterStatus
                        {
                            Id = id,
                            Uri = new Uri(uri),
                            Status = status,
                            IsPartiallySupported = (bool)isPartiallySupported,
                            LastUpdated = (DateTimeOffset)lastUpdated,
                        };

                        // Check whether the corresponding type of the search parameter is supported.
                        SearchParameterInfo paramInfo = null;
                        try
                        {
                            paramInfo = _searchParameterDefinitionManager.GetSearchParameter(resourceSearchParameterStatus.Uri.OriginalString);
                        }
                        catch (SearchParameterNotSupportedException)
                        {
                        }

                        if (paramInfo != null && SqlServerSortingValidator.SupportedSortParamTypes.Contains(paramInfo.Type))
                        {
                            resourceSearchParameterStatus.SortStatus = SortParameterStatus.Enabled;
                        }
                        else
                        {
                            resourceSearchParameterStatus.SortStatus = SortParameterStatus.Disabled;
                        }

                        parameterStatuses.Add(resourceSearchParameterStatus);
                    }
                }

                return parameterStatuses;
            }
        }

        public async Task UpsertStatuses(IReadOnlyCollection<ResourceSearchParameterStatus> statuses, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(statuses, nameof(statuses));

            if (!statuses.Any())
            {
                return;
            }

            // If the search parameter table in SQL does not yet contain the larger status column we reset back to disabled status
            if (_schemaInformation.Current < (int)SchemaVersion.V52)
            {
                foreach (var status in statuses.Where(s => s.Status == SearchParameterStatus.Unsupported))
                {
                    status.Status = SearchParameterStatus.Disabled;
                }
            }

            await UpsertStatusesWithRetry(statuses, 3, cancellationToken);
        }

        private async Task UpsertStatusesWithRetry(IReadOnlyCollection<ResourceSearchParameterStatus> statuses, int maxRetries, CancellationToken cancellationToken)
        {
            var currentStatuses = statuses.ToList();
            int retryCount = 0;

            while (retryCount <= maxRetries)
            {
                try
                {
                    await UpsertStatusesInternal(currentStatuses, cancellationToken);
                    return; // Success
                }
                catch (SqlException sqlEx) when (sqlEx.Number == 50001 && retryCount < maxRetries) // Our custom concurrency error
                {
                    // Optimistic concurrency conflict detected - refresh and retry
                    retryCount++;
                    _logger.LogWarning("Optimistic concurrency conflict detected on attempt {RetryCount}. Retrying...", retryCount);

                    // Refresh the statuses with current LastUpdated values
                    var refreshedStatuses = await GetSearchParameterStatuses(cancellationToken);
                    var refreshedDict = refreshedStatuses.ToDictionary(s => s.Uri.OriginalString, s => s);

                    // Update our statuses with fresh LastUpdated values
                    foreach (var status in currentStatuses)
                    {
                        if (refreshedDict.TryGetValue(status.Uri.OriginalString, out var refreshed))
                        {
                            status.LastUpdated = refreshed.LastUpdated;
                        }
                    }

                    // Wait before retry to reduce contention
                    await Task.Delay(TimeSpan.FromMilliseconds(100.0 * retryCount), cancellationToken);
                }
                catch (SqlException sqlEx) when (sqlEx.Number == 50001)
                {
                    // Max retries exceeded
                    throw new SearchParameterConcurrencyException("Maximum retry attempts exceeded due to concurrency conflicts", sqlEx);
                }
            }
        }

        private async Task UpsertStatusesInternal(IReadOnlyCollection<ResourceSearchParameterStatus> statuses, CancellationToken cancellationToken)
        {
            using (IScoped<SqlConnectionWrapperFactory> scopedSqlConnectionWrapperFactory = _scopedSqlConnectionWrapperFactory.Invoke())
            using (SqlConnectionWrapper sqlConnectionWrapper = await scopedSqlConnectionWrapperFactory.Value.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                if (_schemaInformation.Current >= SchemaVersionConstants.SearchParameterOptimisticConcurrency)
                {
                    VLatest.UpsertSearchParamsWithOptimisticConcurrency.PopulateCommand(sqlCommandWrapper, _updateSearchParamsWithOptimisticConcurrencyTvpGenerator.Generate(statuses.ToList()));
                }
                else
                {
                    VLatest.UpsertSearchParams.PopulateCommand(sqlCommandWrapper, _updateSearchParamsTvpGenerator.Generate(statuses.ToList()));
                }

                using (SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken))
                {
                    while (await sqlDataReader.ReadAsync(cancellationToken))
                    {
                        // The upsert procedure returns the search parameters that were new.
                        (short searchParamId, string searchParamUri) = sqlDataReader.ReadRow(
                            VLatest.SearchParam.SearchParamId,
                            VLatest.SearchParam.Uri);

                        // Read LastUpdated for the inserted/updated parameters
                        DateTimeOffset? lastUpdated = null;
                        try
                        {
                            if (sqlDataReader.FieldCount > 2 && !await sqlDataReader.IsDBNullAsync("LastUpdated", cancellationToken))
                            {
                                lastUpdated = (DateTimeOffset)sqlDataReader["LastUpdated"];
                            }
                        }
                        catch (IndexOutOfRangeException)
                        {
                            // LastUpdated column doesn't exist yet - this can happen during schema migration
                            _logger.LogWarning("LastUpdated column not found in UpsertSearchParams result.");
                            lastUpdated = null;
                        }

                        // Add the new search parameters to the FHIR model dictionary.
                        _fhirModel.TryAddSearchParamIdToUriMapping(searchParamUri, searchParamId);

                        // Update the LastUpdated in our original collection for future operations
                        var matchingStatus = statuses.FirstOrDefault(s => s.Uri.OriginalString == searchParamUri);
                        if (matchingStatus != null && lastUpdated.HasValue)
                        {
                            matchingStatus.LastUpdated = lastUpdated.Value;
                        }
                    }
                }
            }
        }

        // Synchronize the FHIR model dictionary with the data in SQL search parameter status table
        public void SyncStatuses(IReadOnlyCollection<ResourceSearchParameterStatus> statuses)
        {
            foreach (ResourceSearchParameterStatus resourceSearchParameterStatus in statuses)
            {
                var status = (SqlServerResourceSearchParameterStatus)resourceSearchParameterStatus;

                // Add the new search parameters to the FHIR model dictionary.
                _fhirModel.TryAddSearchParamIdToUriMapping(status.Uri.OriginalString, status.Id);
            }
        }

        public async Task<DateTimeOffset> GetMaxLastUpdatedAsync(CancellationToken cancellationToken)
        {
            // If the search parameter table in SQL does not yet contain status columns
            if (_schemaInformation.Current < SchemaVersionConstants.SearchParameterStatusSchemaVersion)
            {
                // Delegate to file-based store
                return await _filebasedSearchParameterStatusDataStore.GetMaxLastUpdatedAsync(cancellationToken);
            }

            using (IScoped<SqlConnectionWrapperFactory> scopedSqlConnectionWrapperFactory = _scopedSqlConnectionWrapperFactory.Invoke())
            using (SqlConnectionWrapper sqlConnectionWrapper = await scopedSqlConnectionWrapperFactory.Value.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                // Use stored procedure if available (V97+), otherwise fall back to direct query
                if (_schemaInformation.Current >= SchemaVersionConstants.SearchParameterMaxLastUpdatedStoredProcedure)
                {
                    VLatest.GetSearchParamMaxLastUpdated.PopulateCommand(sqlCommandWrapper);
                }
                else
                {
                    sqlCommandWrapper.CommandText = "SELECT MAX(LastUpdated) FROM dbo.SearchParam WHERE LastUpdated IS NOT NULL";
                }

                var result = await sqlCommandWrapper.ExecuteScalarAsync(cancellationToken);

                // Handle case where no records exist or all LastUpdated values are null
                if (result == null || result == DBNull.Value)
                {
                    return DateTimeOffset.MinValue;
                }

                return (DateTimeOffset)result;
            }
        }
    }
}
