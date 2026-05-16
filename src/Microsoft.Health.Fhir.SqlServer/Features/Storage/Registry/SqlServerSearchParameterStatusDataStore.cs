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
        public const string SearchParamCacheUpdateProcess = "SearchParamCacheUpdate";

        private readonly ISqlRetryService _sqlRetryService;
        private readonly SchemaInformation _schemaInformation;
        private readonly ISqlServerFhirModel _fhirModel;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly ILogger<SqlServerSearchParameterStatusDataStore> _logger;

        public SqlServerSearchParameterStatusDataStore(
            ISqlRetryService sqlRetryService,
            SchemaInformation schemaInformation,
            ISqlServerFhirModel fhirModel,
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            ILogger<SqlServerSearchParameterStatusDataStore> logger)
        {
            EnsureArg.IsNotNull(sqlRetryService, nameof(sqlRetryService));
            EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
            EnsureArg.IsNotNull(fhirModel, nameof(fhirModel));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _sqlRetryService = sqlRetryService;
            _schemaInformation = schemaInformation;
            _fhirModel = fhirModel;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _logger = logger;
        }

        public string SearchParamCacheUpdateProcessName => SearchParamCacheUpdateProcess;

        public async Task TryLogEvent(string process, string status, string text, DateTime? startDate, CancellationToken cancellationToken)
        {
            await _sqlRetryService.TryLogEvent(process, status, text, startDate, cancellationToken);
        }

        public async Task<IReadOnlyCollection<ResourceSearchParameterStatus>> GetSearchParameterStatuses(CancellationToken cancellationToken, DateTimeOffset? startLastUpdated = null)
        {
            using var cmd = new SqlCommand();
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = "dbo.GetSearchParamStatuses";
            if (startLastUpdated.HasValue)
            {
                cmd.Parameters.AddWithValue("@StartLastUpdated", startLastUpdated.Value);
            }

            var results = await cmd.ExecuteReaderAsync(
                _sqlRetryService,
                (reader) =>
                {
                    (short id, string uri, string stringStatus, DateTimeOffset lastUpdated, bool isPartiallySupported) = reader.ReadRow(
                        VLatest.SearchParam.SearchParamId,
                        VLatest.SearchParam.Uri,
                        VLatest.SearchParam.Status,
                        VLatest.SearchParam.LastUpdated,
                        VLatest.SearchParam.IsPartiallySupported);

                    return (id, uri, stringStatus, lastUpdated, isPartiallySupported);
                },
                _logger,
                cancellationToken);

            var parameterStatuses = new List<ResourceSearchParameterStatus>();
            foreach (var result in results)
            {
                var status = Enum.Parse<SearchParameterStatus>(result.stringStatus, true);

                var resourceSearchParameterStatus = new SqlServerResourceSearchParameterStatus
                {
                    Id = result.id,
                    Uri = new Uri(result.uri),
                    Status = status,
                    IsPartiallySupported = result.isPartiallySupported,
                    LastUpdated = result.lastUpdated,
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

            return parameterStatuses;
        }

        public async Task UpsertStatuses(IReadOnlyCollection<ResourceSearchParameterStatus> statuses, CancellationToken cancellationToken, long? reindexId = null)
        {
            EnsureArg.IsNotNull(statuses, nameof(statuses));

            if (!statuses.Any())
            {
                return;
            }

            using var cmd = new SqlCommand();
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = "dbo.MergeSearchParams";
            cmd.Parameters.AddWithValue("@ReindexId", reindexId ?? 0);
            new SearchParamListTableValuedParameterDefinition("@SearchParams").AddParameter(cmd.Parameters, new SearchParamListRowGenerator().GenerateRows(statuses.ToList()));

            await cmd.ExecuteNonQueryAsync(_sqlRetryService, _logger, cancellationToken);
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

        public async Task<CacheConsistencyResult> CheckCacheConsistencyAsync(DateTime updateEventsSince, DateTime activeHostsSince, CancellationToken cancellationToken)
        {
            if (_schemaInformation.Current < (int)SchemaVersion.V109) // Pre-V109 schemas don't have the sproc; assume inconsistent
            {
                return new CacheConsistencyResult { IsConsistent = false, ActiveHosts = 0, ConvergedHosts = 0 };
            }

            using var cmd = new SqlCommand();
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = "dbo.GetSearchParamCacheUpdateEvents";
            cmd.Parameters.AddWithValue("@UpdateProcess", SearchParamCacheUpdateProcess);
            cmd.Parameters.AddWithValue("@UpdateEventsSince", updateEventsSince);
            cmd.Parameters.AddWithValue("@ActiveHostsSince", activeHostsSince);

            var results = await cmd.ExecuteReaderAsync(
                _sqlRetryService,
                (reader) =>
                {
                    var eventDate = reader.GetDateTime(0);
                    var lastUpdated = reader.IsDBNull(1) ? null : reader.GetString(1); // Use event text as-is because date is saved in a sortable format.
                    var hostName = reader.GetString(2);
                    return (eventDate, lastUpdated, hostName);
                },
                _logger,
                cancellationToken);

            var activeHosts = results.Select(r => r.hostName).ToHashSet();

            // Taking 2 latest events should guarantee that cache completed at least one full update cycle after updateEventsSince.
            var eventsByHosts = results.Where(_ => !string.IsNullOrEmpty(_.lastUpdated))
                                       .GroupBy(_ => _.hostName)
                                       .Select(g => new { g.Key, Value = g.OrderByDescending(_ => _.eventDate).Take(2).ToList() })
                                       .ToDictionary(_ => _.Key, _ => _.Value);
            var updatedHosts = new Dictionary<string, string>();
            foreach (var hostName in activeHosts)
            {
                // There is always a time gap of several milliseconds to several seconds between search indexes generation and surrogate ids assignment.
                // We need to make sure that during this time search param cache does not change.
                // Hence we check that last updated is identical on last two update events.
                if (eventsByHosts.TryGetValue(hostName, out var value) && value.Count == 2 && value[0].lastUpdated == value[1].lastUpdated)
                {
                    updatedHosts.Add(hostName, value[0].lastUpdated);
                }
            }

            var maxLastUpdated = updatedHosts.Values.Max(_ => _);
            var convergedHosts = updatedHosts.Where(_ => _.Value == maxLastUpdated).Select(_ => _.Key).ToList();
            var isConsistent = convergedHosts.Count > 0 && convergedHosts.Count == activeHosts.Count;
            await TryLogEvent("CheckCacheConsistency", "Warn", $"isConsistent={isConsistent} ActiveHosts={activeHosts.Count} ConvergedHosts={convergedHosts.Count}", null, cancellationToken);
            return new CacheConsistencyResult { IsConsistent = isConsistent, ActiveHosts = activeHosts.Count, ConvergedHosts = convergedHosts.Count };
        }
    }
}
