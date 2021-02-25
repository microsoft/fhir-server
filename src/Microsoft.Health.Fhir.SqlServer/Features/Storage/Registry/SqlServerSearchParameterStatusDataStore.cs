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
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Storage;
using SqlDataReader = Microsoft.Data.SqlClient.SqlDataReader;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.Registry
{
    internal class SqlServerSearchParameterStatusDataStore : ISearchParameterStatusDataStore
    {
        private readonly Func<IScoped<SqlConnectionWrapperFactory>> _scopedSqlConnectionWrapperFactory;
        private readonly VLatest.UpsertSearchParamsTvpGenerator<List<ResourceSearchParameterStatus>> _updateSearchParamsTvpGenerator;
        private readonly ISearchParameterStatusDataStore _filebasedSearchParameterStatusDataStore;
        private readonly SchemaInformation _schemaInformation;

        public SqlServerSearchParameterStatusDataStore(
            Func<IScoped<SqlConnectionWrapperFactory>> scopedSqlConnectionWrapperFactory,
            VLatest.UpsertSearchParamsTvpGenerator<List<ResourceSearchParameterStatus>> updateSearchParamsTvpGenerator,
            FilebasedSearchParameterStatusDataStore.Resolver filebasedRegistry,
            SchemaInformation schemaInformation)
        {
            EnsureArg.IsNotNull(scopedSqlConnectionWrapperFactory, nameof(scopedSqlConnectionWrapperFactory));
            EnsureArg.IsNotNull(updateSearchParamsTvpGenerator, nameof(updateSearchParamsTvpGenerator));
            EnsureArg.IsNotNull(filebasedRegistry, nameof(filebasedRegistry));
            EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));

            _scopedSqlConnectionWrapperFactory = scopedSqlConnectionWrapperFactory;
            _updateSearchParamsTvpGenerator = updateSearchParamsTvpGenerator;
            _filebasedSearchParameterStatusDataStore = filebasedRegistry.Invoke();
            _schemaInformation = schemaInformation;
        }

        // TODO: Make cancellation token an input.
        public async Task<IReadOnlyCollection<ResourceSearchParameterStatus>> GetSearchParameterStatuses()
        {
            // If the search parameter table in SQL does not yet contain status columns
            if (_schemaInformation.Current < SchemaVersionConstants.SearchParameterStatusSchemaVersion)
            {
                // Get status information from file.
                return await _filebasedSearchParameterStatusDataStore.GetSearchParameterStatuses();
            }

            using (IScoped<SqlConnectionWrapperFactory> scopedSqlConnectionWrapperFactory = _scopedSqlConnectionWrapperFactory())
            using (SqlConnectionWrapper sqlConnectionWrapper = await scopedSqlConnectionWrapperFactory.Value.ObtainSqlConnectionWrapperAsync(CancellationToken.None, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateSqlCommand())
            {
                VLatest.GetSearchParamStatuses.PopulateCommand(sqlCommandWrapper);

                var parameterStatuses = new List<ResourceSearchParameterStatus>();

                using (SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(CommandBehavior.SequentialAccess, CancellationToken.None))
                {
                    while (await sqlDataReader.ReadAsync())
                    {
                        (string uri, string stringStatus, DateTimeOffset? lastUpdated, bool? isPartiallySupported) = sqlDataReader.ReadRow(
                            VLatest.SearchParam.Uri,
                            VLatest.SearchParam.Status,
                            VLatest.SearchParam.LastUpdated,
                            VLatest.SearchParam.IsPartiallySupported);

                        if (string.IsNullOrEmpty(stringStatus) || lastUpdated == null || isPartiallySupported == null)
                        {
                            // These columns are nullable because they are added to dbo.SearchParam in a later schema version.
                            // They should be populated as soon as they are added to the table and should never be null.
                            throw new NullReferenceException(Resources.SearchParameterStatusShouldNotBeNull);
                        }

                        var status = Enum.Parse<SearchParameterStatus>(stringStatus, true);

                        var resourceSearchParameterStatus = new ResourceSearchParameterStatus()
                        {
                            Uri = new Uri(uri),
                            Status = status,
                            IsPartiallySupported = (bool)isPartiallySupported,
                            LastUpdated = (DateTimeOffset)lastUpdated,
                        };

                        parameterStatuses.Add(resourceSearchParameterStatus);
                    }
                }

                return parameterStatuses;
            }
        }

        // TODO: Make cancellation token an input.
        public async Task UpsertStatuses(List<ResourceSearchParameterStatus> statuses)
        {
            EnsureArg.IsNotNull(statuses, nameof(statuses));

            if (!statuses.Any())
            {
                return;
            }

            if (_schemaInformation.Current < SchemaVersionConstants.SearchParameterStatusSchemaVersion)
            {
                throw new BadRequestException(Resources.SchemaVersionNeedsToBeUpgraded);
            }

            using (IScoped<SqlConnectionWrapperFactory> scopedSqlConnectionWrapperFactory = _scopedSqlConnectionWrapperFactory())
            using (SqlConnectionWrapper sqlConnectionWrapper = await scopedSqlConnectionWrapperFactory.Value.ObtainSqlConnectionWrapperAsync(CancellationToken.None, true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateSqlCommand())
            {
                VLatest.UpsertSearchParams.PopulateCommand(sqlCommandWrapper, _updateSearchParamsTvpGenerator.Generate(statuses));

                await sqlCommandWrapper.ExecuteNonQueryAsync(CancellationToken.None);
            }
        }
    }
}
