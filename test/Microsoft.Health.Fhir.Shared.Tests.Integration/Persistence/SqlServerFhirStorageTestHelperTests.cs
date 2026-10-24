// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Identity;
using Medino;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Configs;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Storage;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    /// <summary>
    /// Protects fixture database creation and cleanup from shared connection-pool exhaustion.
    /// xUnit reports class-fixture cleanup failures separately, so Azure DevOps can retain a failed
    /// cleanup result even after an MTP retry passes. See https://github.com/microsoft/testfx/issues/4087.
    /// Cleanup must retry the original database before fixture disposal fails; a new test attempt
    /// creates a different database and does not prove that the original database was deleted.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class SqlServerFhirStorageTestHelperTests
    {
        [Fact]
        [Trait("DataStore", "SqlServer")]
        public async Task GivenExhaustedMasterPool_WhenDeletingDatabase_TargetIsDeletedAndMasterPoolSurvives()
        {
            var connectionString = new SqlConnectionStringBuilder(EnvironmentVariables.GetEnvironmentVariable(KnownEnvironmentVariableNames.SqlServerConnectionString))
            {
                InitialCatalog = "master",
                MaxPoolSize = 1,
                Pooling = true,
                ApplicationName = Guid.NewGuid().ToString(),
            };
            string clientId = EnvironmentVariables.GetEnvironmentVariable(KnownEnvironmentVariableNames.AzureSubscriptionClientId);
            string tenantId = EnvironmentVariables.GetEnvironmentVariable(KnownEnvironmentVariableNames.AzureSubscriptionTenantId);
            string serviceConnectionId = EnvironmentVariables.GetEnvironmentVariable(KnownEnvironmentVariableNames.AzureSubscriptionServiceConnectionId);
            string systemAccessToken = EnvironmentVariables.GetEnvironmentVariable(KnownEnvironmentVariableNames.SystemAccessToken);
            if (!string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(tenantId) && !string.IsNullOrEmpty(serviceConnectionId) && !string.IsNullOrEmpty(systemAccessToken))
            {
                var credential = new AzurePipelinesCredential(tenantId, clientId, serviceConnectionId, systemAccessToken);
                SqlAuthenticationProvider.SetProvider(SqlAuthenticationMethod.ActiveDirectoryWorkloadIdentity, new SqlAzurePipelinesWorkloadIdentityAuthenticationProvider(credential));
            }

            var config = Options.Create(new SqlServerDataStoreConfiguration { ConnectionString = connectionString.ConnectionString });
            var builder = new DefaultSqlConnectionBuilder(config, SqlConfigurableRetryFactory.CreateFixedRetryProvider(new SqlClientRetryOptions().Settings));
            var helper = CreateHelper(builder);
            string databaseName = SqlServerFhirStorageTestsFixture.GetDatabaseName("Lifecycle");
            await using var master = await builder.GetSqlConnectionAsync("master");
            await master.OpenAsync();
            Guid masterConnectionId = master.ClientConnectionId;
            try
            {
                await using (var create = master.CreateCommand())
                {
                    create.CommandTimeout = 300;
                    create.CommandText = $"CREATE DATABASE {databaseName}";
                    await create.ExecuteNonQueryAsync();
                }

                await using (var target = await builder.GetSqlConnectionAsync(databaseName))
                {
                    await target.OpenAsync();
                }

                await helper.DeleteDatabase(databaseName);

                await using (var exists = master.CreateCommand())
                {
                    exists.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE name = @name";
                    exists.Parameters.AddWithValue("@name", databaseName);
                    Assert.Equal(0, (int)await exists.ExecuteScalarAsync());
                }

                await master.CloseAsync();
                await master.OpenAsync();
                Assert.Equal(masterConnectionId, master.ClientConnectionId);
            }
            finally
            {
                if (master.State != System.Data.ConnectionState.Open)
                {
                    await master.OpenAsync();
                }

                await using var cleanup = master.CreateCommand();
                cleanup.CommandTimeout = 300;
                cleanup.CommandText = $"DROP DATABASE IF EXISTS {databaseName}";
                await cleanup.ExecuteNonQueryAsync();
                SqlConnection.ClearPool(master);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GivenCancelledLifecycleOperation_WhenStarted_NoConnectionIsAcquired(bool create)
        {
            var builder = Substitute.For<ISqlConnectionBuilder>();
            var helper = CreateHelper(builder);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ExecuteLifecycle(helper, create, cancellation.Token));

            Assert.Empty(builder.ReceivedCalls());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GivenTerminalConnectionFailure_WhenLifecycleRepeats_FailurePropagatesAndGateIsReleased(bool create)
        {
            var builder = Substitute.For<ISqlConnectionBuilder>();
            var failure = new InvalidOperationException("Terminal connection failure");
            builder.GetSqlConnectionAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult(new SqlConnection("Server=unused;Initial Catalog=fixture")));
            builder.CreateConnectionAsync(Arg.Any<Action<SqlConnectionStringBuilder>>(), Arg.Any<CancellationToken>())
                .Returns<SqlConnection>(_ => throw failure);
            var helper = CreateHelper(builder);
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            for (var attempt = 0; attempt < 15; attempt++)
            {
                var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => ExecuteLifecycle(helper, create, cancellation.Token));
                Assert.Same(failure, actual);
            }

            await builder.Received(15).CreateConnectionAsync(Arg.Any<Action<SqlConnectionStringBuilder>>(), cancellation.Token);
        }

        [Fact]
        public async Task GivenTransientConnectionFailure_WhenDeleteRetriesExhausted_SameDatabaseIsRetriedAndFailurePropagates()
        {
            var builder = Substitute.For<ISqlConnectionBuilder>();
            var failure = new TimeoutException("Transient connection failure");
            builder.GetSqlConnectionAsync("fixture", null, Arg.Any<CancellationToken>())
                .Returns(_ => Task.FromResult(new SqlConnection("Server=unused;Initial Catalog=fixture")));
            builder.CreateConnectionAsync(Arg.Any<Action<SqlConnectionStringBuilder>>(), Arg.Any<CancellationToken>())
                .Returns<SqlConnection>(_ => throw failure);
            var helper = CreateHelper(builder);

            var actual = await Assert.ThrowsAsync<TimeoutException>(() => helper.DeleteDatabase("fixture"));

            Assert.Same(failure, actual);
            await builder.Received(6).GetSqlConnectionAsync("fixture", null, CancellationToken.None);
            await builder.Received(6).CreateConnectionAsync(Arg.Any<Action<SqlConnectionStringBuilder>>(), CancellationToken.None);
        }

        private static Task ExecuteLifecycle(SqlServerFhirStorageTestHelper helper, bool create, CancellationToken cancellationToken)
        {
            return create
                ? helper.CreateAndInitializeDatabase("fixture", 1, cancellationToken)
                : helper.DeleteDatabase("fixture", cancellationToken);
        }

        private static SqlServerFhirStorageTestHelper CreateHelper(ISqlConnectionBuilder builder)
        {
            var schema = new SchemaInformation(1, 1);
            var model = new SqlServerFhirModel(
                schema,
                Substitute.For<ISearchParameterDefinitionManager>(),
                () => null,
                Options.Create(new SecurityConfiguration()),
                Substitute.For<IScopeProvider<SqlConnectionWrapperFactory>>(),
                Substitute.For<IMediator>(),
                Substitute.For<ISqlRetryService>(),
                ResourceIdPolicy.Standard,
                NullLogger<SqlServerFhirModel>.Instance);
            return new SqlServerFhirStorageTestHelper("Server=unused", "master", model, builder, null, schema);
        }
    }
}
