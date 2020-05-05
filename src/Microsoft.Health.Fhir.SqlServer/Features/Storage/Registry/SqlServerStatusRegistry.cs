// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Storage;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.Registry
{
    public class SqlServerStatusRegistry : ISearchParameterRegistry
    {
        private readonly SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;

        public SqlServerStatusRegistry(SqlConnectionWrapperFactory sqlConnectionWrapperFactory)
        {
            EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));

            _sqlConnectionWrapperFactory = sqlConnectionWrapperFactory;
        }

        public async Task<IReadOnlyCollection<ResourceSearchParameterStatus>> GetSearchParameterStatuses()
        {
            using (SqlConnectionWrapper sqlConnectionWrapper = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapper(true))
            using (SqlCommand sqlCommand = sqlConnectionWrapper.CreateSqlCommand())
            {
                VLatest.GetSearchParameterStatuses.PopulateCommand(sqlCommand);

                var parameterStatuses = new List<ResourceSearchParameterStatus>();

                // TODO: Pass in cancellation token?
                using (SqlDataReader sqlDataReader = await sqlCommand.ExecuteReaderAsync(CommandBehavior.SequentialAccess))
                {
                    while (await sqlDataReader.ReadAsync())
                    {
                        // TODO: Fix data types, avoid weird conversions.
                        (int uri, string stringStatus, DateTimeOffset? lastUpdated) = sqlDataReader.ReadRow(
                            VLatest.SearchParameterRegistry.ResourceTypeId,
                            VLatest.SearchParameterRegistry.Status,
                            VLatest.SearchParameterRegistry.LastUpdated);

                        var status = Enum.Parse<SearchParameterStatus>(stringStatus, true);

                        var resourceSearchParameterStatus = new ResourceSearchParameterStatus()
                        {
                            Uri = new Uri(uri.ToString()),
                            Status = status,
                            IsPartiallySupported = status == SearchParameterStatus.Supported &&
                                                   status != SearchParameterStatus.Enabled,
                            LastUpdated = (DateTimeOffset)lastUpdated,
                        };

                        parameterStatuses.Add(resourceSearchParameterStatus);
                    }
                }

                return parameterStatuses;
            }
        }

        public Task UpdateStatuses(IEnumerable<ResourceSearchParameterStatus> statuses)
        {
            throw new NotImplementedException();
        }
    }
}
