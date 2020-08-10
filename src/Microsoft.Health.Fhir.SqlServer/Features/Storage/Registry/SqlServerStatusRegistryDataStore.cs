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
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.Registry
{
    internal class SqlServerStatusRegistryDataStore : IStatusRegistryDataStore
    {
        private readonly Func<IScoped<SqlConnectionWrapperFactory>> _scopedSqlConnectionWrapperFactory;
        private readonly VLatest.UpsertSearchParamsTvpGenerator<List<ResourceSearchParameterStatus>> _updateSearchParamsTvpGenerator;

        public SqlServerStatusRegistryDataStore(
            Func<IScoped<SqlConnectionWrapperFactory>> scopedSqlConnectionWrapperFactory,
            VLatest.UpsertSearchParamsTvpGenerator<List<ResourceSearchParameterStatus>> updateSearchParamsTvpGenerator)
        {
            EnsureArg.IsNotNull(scopedSqlConnectionWrapperFactory, nameof(scopedSqlConnectionWrapperFactory));
            EnsureArg.IsNotNull(updateSearchParamsTvpGenerator, nameof(updateSearchParamsTvpGenerator));

            _scopedSqlConnectionWrapperFactory = scopedSqlConnectionWrapperFactory;
            _updateSearchParamsTvpGenerator = updateSearchParamsTvpGenerator;
        }

        public async Task<IReadOnlyCollection<ResourceSearchParameterStatus>> GetSearchParameterStatuses()
        {
            using (IScoped<SqlConnectionWrapperFactory> scopedSqlConnectionWrapperFactory = _scopedSqlConnectionWrapperFactory())
            using (SqlConnectionWrapper sqlConnectionWrapper = scopedSqlConnectionWrapperFactory.Value.ObtainSqlConnectionWrapper(true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateSqlCommand())
            {
                VLatest.GetSearchParamStatuses.PopulateCommand(sqlCommandWrapper);

                var parameterStatuses = new List<ResourceSearchParameterStatus>();

                // TODO: Make cancellation token an input.
                using (SqlDataReader sqlDataReader = await sqlCommandWrapper.ExecuteReaderAsync(CommandBehavior.SequentialAccess, CancellationToken.None))
                {
                    while (await sqlDataReader.ReadAsync())
                    {
                        (string uri, string stringStatus, DateTimeOffset lastUpdated, bool isPartiallySupported) = sqlDataReader.ReadRow(
                            VLatest.SearchParam.Uri,
                            VLatest.SearchParam.Status,
                            VLatest.SearchParam.LastUpdated,
                            VLatest.SearchParam.IsPartiallySupported);

                        var status = Enum.Parse<SearchParameterStatus>(stringStatus, true);

                        var resourceSearchParameterStatus = new ResourceSearchParameterStatus()
                        {
                            Uri = new Uri(uri),
                            Status = status,
                            IsPartiallySupported = isPartiallySupported,
                            LastUpdated = lastUpdated,
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

            using (IScoped<SqlConnectionWrapperFactory> scopedSqlConnectionWrapperFactory = _scopedSqlConnectionWrapperFactory())
            using (SqlConnectionWrapper sqlConnectionWrapper = scopedSqlConnectionWrapperFactory.Value.ObtainSqlConnectionWrapper(true))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateSqlCommand())
            {
                VLatest.UpsertSearchParams.PopulateCommand(sqlCommandWrapper, _updateSearchParamsTvpGenerator.Generate(statuses.ToList()));

                await sqlCommandWrapper.ExecuteNonQueryAsync(CancellationToken.None);
            }
        }
    }
}
