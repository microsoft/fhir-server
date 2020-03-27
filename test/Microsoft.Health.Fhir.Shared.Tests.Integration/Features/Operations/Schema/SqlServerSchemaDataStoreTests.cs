// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Tests.Integration.Features.Operations.Schema
{
    [CollectionDefinition("SqlServerSchemaDataStoreTests", DisableParallelization = true)]
    [FhirStorageTestsFixtureArgumentSets(DataStore.SqlServer)]
    public class SqlServerSchemaDataStoreTests : IClassFixture<SqlServerFhirStorageTestsFixture>, IAsyncLifetime
    {
        private readonly string _connectionString;
        private readonly string _name;
        private readonly SqlServerSchemaDataStore _sqlServerSchemaDataStore;
        private readonly CancellationToken _cancellationToken = default;
        private readonly SchemaInformation _schemaInformation = new SchemaInformation();

        public SqlServerSchemaDataStoreTests(SqlServerFhirStorageTestsFixture fixture)
        {
            _connectionString = fixture.TestConnectionString;
            _name = Guid.NewGuid() + "-" + Process.GetCurrentProcess().Id.ToString();
            _sqlServerSchemaDataStore = new SqlServerSchemaDataStore(fixture.SqlConnectionWrapperFactory, NullLogger<SqlServerSchemaDataStore>.Instance);
        }

        public Task InitializeAsync()
        {
            if (_schemaInformation.Current == null)
            {
                _schemaInformation.Current = _schemaInformation.MinimumSupportedVersion;
            }

            return DeleteInstanceSchemaRecordAsync(_name);
        }

        public Task DisposeAsync()
        {
            return Task.CompletedTask;
        }

        [Fact]
        public async Task GivenThereIsNoRecord_WhenCreatingAnInstanceSchemaRecord_ThenNewRecordCreated()
        {
            await _sqlServerSchemaDataStore.InsertInstanceSchemaInformation(_name, _schemaInformation, _cancellationToken);
            Assert.Equal(1, await SelectInstanceSchemaRecordAsync());
        }

        [Fact]
        public async Task GivenThereIsARecord_WhenUpsertingAnInstanceSchemaRecord_ThenRecordUpdated()
        {
            await _sqlServerSchemaDataStore.UpsertInstanceSchemaInformation(_name, new CompatibleVersions(1, 3), 2, _cancellationToken);
            Assert.Equal(1, await SelectInstanceSchemaRecordAsync());
        }

        private async Task DeleteInstanceSchemaRecordAsync(string name, CancellationToken cancellationToken = default)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                var command = new SqlCommand("DELETE FROM dbo.InstanceSchema WHERE name=@name", connection);

                var parameter = new SqlParameter { ParameterName = "@name", Value = name };
                command.Parameters.Add(parameter);

                await command.Connection.OpenAsync(cancellationToken);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        private async Task<int> SelectInstanceSchemaRecordAsync(CancellationToken cancellationToken = default)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                var command = new SqlCommand("Select count(*) FROM dbo.InstanceSchema WHERE name=@name", connection);

                var parameter = new SqlParameter { ParameterName = "@name", Value = _name };
                command.Parameters.Add(parameter);

                await command.Connection.OpenAsync(cancellationToken);
                return (int)await command.ExecuteScalarAsync(cancellationToken);
            }
        }
    }
}
