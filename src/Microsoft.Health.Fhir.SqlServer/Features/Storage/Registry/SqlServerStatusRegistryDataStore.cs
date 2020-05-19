// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.Registry
{
    internal class SqlServerStatusRegistryDataStore : ISearchParameterRegistryDataStore
    {
        private readonly SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private readonly VLatest.UpsertSearchParamStatusTvpGenerator<List<ResourceSearchParameterStatus>> _updateSearchParamRegistryTvpGenerator;
        private readonly VLatest.InsertIntoSearchParamRegistryTvpGenerator<List<ResourceSearchParameterStatus>> _insertSearchParamRegistryTvpGenerator;

        public SqlServerStatusRegistryDataStore(
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

        public async Task<IReadOnlyCollection<ResourceSearchParameterStatus>> GetSearchParameterStatuses(CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapper(true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateSqlCommand())
            {
                VLatest.GetSearchParamStatuses.PopulateCommand(sqlCommandWrapper);

                var parameterStatuses = new List<ResourceSearchParameterStatus>();

                using (SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken))
                {
                    while (await sqlDataReader.ReadAsync())
                    {
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

        public async Task UpsertStatuses(IEnumerable<ResourceSearchParameterStatus> statuses, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(statuses, nameof(statuses));

            using (SqlConnectionWrapper sqlConnectionWrapper = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapper(true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateSqlCommand())
            {
                VLatest.UpsertSearchParamStatus.PopulateCommand(sqlCommandWrapper, _updateSearchParamRegistryTvpGenerator.Generate(statuses.ToList()));

                await sqlCommandWrapper.ExecuteScalarAsync(cancellationToken);
            }
        }

        internal async Task<bool> IsSearchParameterRegistryEmpty(CancellationToken cancellationToken)
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapper(true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateSqlCommand())
            {
                VLatest.GetSearchParamRegistryCount.PopulateCommand(sqlCommandWrapper);

                return (int)await sqlCommandWrapper.ExecuteScalarAsync(cancellationToken) == 0;
            }
        }

        internal async Task BulkInsert(List<ResourceSearchParameterStatus> statuses, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(statuses, nameof(statuses));

            using (SqlConnectionWrapper sqlConnectionWrapper = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapper(true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateSqlCommand())
            {
                VLatest.InsertIntoSearchParamRegistry.PopulateCommand(sqlCommandWrapper, _insertSearchParamRegistryTvpGenerator.Generate(statuses));

                await sqlCommandWrapper.ExecuteScalarAsync(cancellationToken);
            }
        }
    }
}
