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

        public async Task UpsertStatuses(IReadOnlyCollection<ResourceSearchParameterStatus> statuses, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(statuses, nameof(statuses));

            if (!statuses.Any())
            {
                return;
            }

            using var cmd = new SqlCommand();
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.CommandText = "dbo.MergeSearchParams";
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
    }
}
