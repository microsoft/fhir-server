// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data.SqlClient;
using EnsureThat;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Configs;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.Registry
{
    public class SqlServerStatusRegistryInitializer : IStartable
    {
        private readonly ISearchParameterRegistry _filebasedRegistry;
        private readonly SqlServerDataStoreConfiguration _sqlServerDataStoreConfiguration;

        public SqlServerStatusRegistryInitializer(FilebasedSearchParameterRegistry.Resolver filebasedRegistry, SqlServerDataStoreConfiguration sqlServerDataStoreConfiguration)
        {
            EnsureArg.IsNotNull(filebasedRegistry, nameof(filebasedRegistry));
            EnsureArg.IsNotNull(sqlServerDataStoreConfiguration, nameof(sqlServerDataStoreConfiguration));

            _filebasedRegistry = filebasedRegistry.Invoke();
            _sqlServerDataStoreConfiguration = sqlServerDataStoreConfiguration;
        }

        public async void Start() // TODO: Should a start method be async?
        {
            using (var sqlConnection = new SqlConnection(_sqlServerDataStoreConfiguration.ConnectionString))
            {
                sqlConnection.Open();
                int rowCount = 0;
                using (SqlCommand command = sqlConnection.CreateCommand())
                {
                    command.CommandText = "SELECT COUNT(*) FROM dbo.SearchParamRegistry";
                    rowCount = (int)await command.ExecuteScalarAsync();
                }

                if (rowCount == 0)
                {
                    var statuses = await _filebasedRegistry.GetSearchParameterStatuses();

                    using (SqlCommand command = sqlConnection.CreateCommand())
                    {
                        foreach (ResourceSearchParameterStatus status in statuses)
                        {
                            VLatest.InsertIntoSearchParamRegistry.PopulateCommand(
                                command,
                                status.Uri.ToString(),
                                status.Status.ToString(),
                                status.IsPartiallySupported);

                            await command.ExecuteScalarAsync();

                            // Clear the parameters for the next loop.
                            command.Parameters.Clear();
                        }
                    }
                }
            }
        }
    }
}
