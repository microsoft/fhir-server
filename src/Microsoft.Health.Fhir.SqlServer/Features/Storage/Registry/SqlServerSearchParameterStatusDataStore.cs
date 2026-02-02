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
using Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Storage;
using SqlDataReader = Microsoft.Data.SqlClient.SqlDataReader;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.Registry
{
    internal class SqlServerSearchParameterStatusDataStore : ISearchParameterStatusDataStore
    {
        private readonly IScopeProvider<SqlConnectionWrapperFactory> _scopedSqlConnectionWrapperFactory;
        private readonly SchemaInformation _schemaInformation;
        private readonly SqlServerSortingValidator _sortingValidator;
        private readonly ISqlServerFhirModel _fhirModel;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly ILogger<SqlServerSearchParameterStatusDataStore> _logger;

        public SqlServerSearchParameterStatusDataStore(
            IScopeProvider<SqlConnectionWrapperFactory> scopedSqlConnectionWrapperFactory,
            SchemaInformation schemaInformation,
            SqlServerSortingValidator sortingValidator,
            ISqlServerFhirModel fhirModel,
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            ILogger<SqlServerSearchParameterStatusDataStore> logger)
        {
            EnsureArg.IsNotNull(scopedSqlConnectionWrapperFactory, nameof(scopedSqlConnectionWrapperFactory));
            EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
            EnsureArg.IsNotNull(sortingValidator, nameof(sortingValidator));
            EnsureArg.IsNotNull(fhirModel, nameof(fhirModel));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _scopedSqlConnectionWrapperFactory = scopedSqlConnectionWrapperFactory;
            _schemaInformation = schemaInformation;
            _sortingValidator = sortingValidator;
            _fhirModel = fhirModel;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _logger = logger;
        }

        public async Task<IReadOnlyCollection<ResourceSearchParameterStatus>> GetSearchParameterStatuses(CancellationToken cancellationToken)
        {
            using (IScoped<SqlConnectionWrapperFactory> scopedSqlConnectionWrapperFactory = _scopedSqlConnectionWrapperFactory.Invoke())
            using (SqlConnectionWrapper sqlConnectionWrapper = await scopedSqlConnectionWrapperFactory.Value.ObtainSqlConnectionWrapperAsync(cancellationToken, true))
            using (SqlCommandWrapper cmd = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandText = "dbo.GetSearchParamStatuses";

                var parameterStatuses = new List<ResourceSearchParameterStatus>();

                // TODO: Bad reader. Use SQL retry
                using (SqlDataReader sqlDataReader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken))
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
                            // TODO: This means that definitions are not in sync with statuses
                            // If we leave it and update high water mark we might miss statuses forever
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
            using (SqlCommandWrapper cmd = sqlConnectionWrapper.CreateRetrySqlCommand())
            {
                cmd.CommandType = CommandType.StoredProcedure;
                if (_schemaInformation.Current >= 103)
                {
                    cmd.CommandText = "dbo.MergeSearchParams";
                }
                else
                {
                    cmd.CommandText = "dbo.UpsertSearchParamsWithOptimisticConcurrency";
                }

                new SearchParamListTableValuedParameterDefinition("@SearchParams").AddParameter(cmd.Parameters, new SearchParamListRowGenerator().GenerateRows(statuses.ToList()));

                // TODO: Reader is not propagating all failures to the code
                using (SqlDataReader sqlDataReader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken))
                {
                    while (await sqlDataReader.ReadAsync(cancellationToken))
                    {
                        // The procedure returns new search parameters.
                        (short searchParamId, string searchParamUri, DateTimeOffset lastUpdated) = sqlDataReader.ReadRow(VLatest.SearchParam.SearchParamId, VLatest.SearchParam.Uri, VLatest.SearchParam.LastUpdated);

                        // Add the new search parameters to the FHIR model dictionary.
                        _fhirModel.TryAddSearchParamIdToUriMapping(searchParamUri, searchParamId);

                        // Update the LastUpdated in our original collection for future operations
                        // TODO: We are returning only new statuses, do we need update LastUpdated on "old" statuses
                        var matchingStatus = statuses.FirstOrDefault(s => s.Uri.OriginalString == searchParamUri);
                        if (matchingStatus != null)
                        {
                            matchingStatus.LastUpdated = lastUpdated;
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
            // TODO: use sql retry class
            using IScoped<SqlConnectionWrapperFactory> scopedSqlConnectionWrapperFactory = _scopedSqlConnectionWrapperFactory.Invoke();
            using SqlConnectionWrapper sqlConnectionWrapper = await scopedSqlConnectionWrapperFactory.Value.ObtainSqlConnectionWrapperAsync(cancellationToken, true);
            using SqlCommandWrapper cmd = sqlConnectionWrapper.CreateRetrySqlCommand();
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = "dbo.GetSearchParamMaxLastUpdated";
            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            return (result == null || result == DBNull.Value) ? DateTimeOffset.MinValue : (DateTimeOffset)result;
        }
    }
}
