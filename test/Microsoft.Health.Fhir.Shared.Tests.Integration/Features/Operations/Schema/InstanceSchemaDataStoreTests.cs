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
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Tests.Integration.Features.Operations.Schema
{
    [CollectionDefinition("InstanceSchemaDataStoreTests", DisableParallelization = true)]
    [FhirStorageTestsFixtureArgumentSets(DataStore.SqlServer)]
    public class InstanceSchemaDataStoreTests : IClassFixture<SqlServerFhirStorageTestsFixture>, IAsyncLifetime
    {
        private readonly string _connectionString;
        private readonly string _name;
        private readonly InstanceSchemaDataStore _instanceSchemaDataStore;
        private readonly SchemaInformation _schemaInformation = new SchemaInformation();

        public InstanceSchemaDataStoreTests(SqlServerFhirStorageTestsFixture fixture)
        {
            _connectionString = fixture.TestConnectionString;
            _name = Guid.NewGuid() + "-" + Process.GetCurrentProcess().Id.ToString();
            _instanceSchemaDataStore = new InstanceSchemaDataStore(fixture.SqlServerDataStoreConfiguration, NullLogger<InstanceSchemaDataStore>.Instance);
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
        public void GivenThereIsNoRecord_WhenCreatingAnInstanceSchemaRecord_ThenNewRecordCreated()
        {
            string name = _instanceSchemaDataStore.InsertInstanceSchemaInformation(_schemaInformation);
            Assert.NotNull(name);
        }

        [Fact]
        public async Task GivenThereIsARecord_WhenUpsertingAnInstanceSchemaRecord_ThenRecordUpserted()
        {
            string name = await _instanceSchemaDataStore.UpsertInstanceSchemaInformation(new CompatibleVersions(1, 3), 2);
            Assert.NotNull(name);
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
    }
}
