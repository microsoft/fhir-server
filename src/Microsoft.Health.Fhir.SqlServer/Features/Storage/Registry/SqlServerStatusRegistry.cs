// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.Registry
{
    internal class SqlServerStatusRegistry : ISearchParameterRegistry
    {
        private readonly SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private readonly VLatest.UpsertSearchParamStatusTvpGenerator<List<ResourceSearchParameterStatus>> _updateSearchParamRegistryTvpGenerator;
        private readonly VLatest.InsertIntoSearchParamRegistryTvpGenerator<List<ResourceSearchParameterStatus>> _insertSearchParamRegistryTvpGenerator;

        public SqlServerStatusRegistry(
            SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
            VLatest.UpsertSearchParamStatusTvpGenerator<List<ResourceSearchParameterStatus>> updateSearchParamRegistryTvpGenerator,
            VLatest.InsertIntoSearchParamRegistryTvpGenerator<List<ResourceSearchParameterStatus>> insertSearchParamRegistryTvpGenerator)
        {
            EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            EnsureArg.IsNotNull(updateSearchParamRegistryTvpGenerator, nameof(updateSearchParamRegistryTvpGenerator));
            EnsureArg.IsNotNull(insertSearchParamRegistryTvpGenerator, nameof(insertSearchParamRegistryTvpGenerator));

            _sqlConnectionWrapperFactory = sqlConnectionWrapperFactory;
            _updateSearchParamRegistryTvpGenerator = updateSearchParamRegistryTvpGenerator;
            _insertSearchParamRegistryTvpGenerator = insertSearchParamRegistryTvpGenerator;
        }

        public async Task<IReadOnlyCollection<ResourceSearchParameterStatus>> GetSearchParameterStatuses()
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapper(true))
            using (SqlCommand sqlCommand = sqlConnectionWrapper.CreateSqlCommand())
            {
                VLatest.GetSearchParamStatuses.PopulateCommand(sqlCommand);

                var parameterStatuses = new List<ResourceSearchParameterStatus>();

                // TODO: Pass in cancellation token?
                using (SqlDataReader sqlDataReader = await sqlCommand.ExecuteReaderAsync(CommandBehavior.SequentialAccess))
                {
                    while (await sqlDataReader.ReadAsync())
                    {
                        // TODO: Fix data types, avoid weird conversions.
                        (string uri, string stringStatus, DateTimeOffset? lastUpdated, bool isPartiallySupported) = sqlDataReader.ReadRow(
                            VLatest.SearchParamRegistry.Uri,
                            VLatest.SearchParamRegistry.Status,
                            VLatest.SearchParamRegistry.LastUpdated,
                            VLatest.SearchParamRegistry.IsPartiallySupported);

                        var status = Enum.Parse<SearchParameterStatus>(stringStatus, true);

                        var resourceSearchParameterStatus = new ResourceSearchParameterStatus()
                        {
                            Uri = new Uri(uri),
                            Status = status,
                            IsPartiallySupported = isPartiallySupported,
                            LastUpdated = (DateTimeOffset)lastUpdated,
                        };

                        parameterStatuses.Add(resourceSearchParameterStatus);
                    }
                }

                return parameterStatuses;
            }
        }

        public async Task UpsertStatuses(IEnumerable<ResourceSearchParameterStatus> statuses)
        {
            EnsureArg.IsNotNull(statuses, nameof(statuses));

            using (SqlConnectionWrapper sqlConnectionWrapper = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapper(true))
            using (SqlCommand sqlCommand = sqlConnectionWrapper.CreateSqlCommand())
            {
                VLatest.UpsertSearchParamStatus.PopulateCommand(sqlCommand, _updateSearchParamRegistryTvpGenerator.Generate(statuses.ToList()));

                await sqlCommand.ExecuteScalarAsync();
            }
        }

        internal async Task<bool> GetIsSearchParameterRegistryEmpty()
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapper(true))
            using (SqlCommand sqlCommand = sqlConnectionWrapper.CreateSqlCommand())
            {
                VLatest.GetSearchParamRegistryCount.PopulateCommand(sqlCommand);

                return (int)await sqlCommand.ExecuteScalarAsync() == 0;
            }
        }

        internal async Task BulkInsert(List<ResourceSearchParameterStatus> statuses)
        {
            EnsureArg.IsNotNull(statuses, nameof(statuses));

            using (SqlConnectionWrapper sqlConnectionWrapper = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapper(true))
            using (SqlCommand sqlCommand = sqlConnectionWrapper.CreateSqlCommand())
            {
                VLatest.InsertIntoSearchParamRegistry.PopulateCommand(sqlCommand, _insertSearchParamRegistryTvpGenerator.Generate(statuses));

                await sqlCommand.ExecuteScalarAsync();
            }
        }
    }
}
