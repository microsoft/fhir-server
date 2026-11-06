// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Tenants;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Configs;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Storage
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class SqlTenantDatabaseResolverTests
    {
        private readonly FhirRequestContextAccessor _accessor = new FhirRequestContextAccessor();
        private readonly SqlTenantDatabaseResolver _resolver;

        public SqlTenantDatabaseResolverTests()
        {
            var configuration = new TenantConfiguration();
            configuration.Tenants.Add(new TenantInfo { Id = "tenant-a", DatabaseName = "database.a]" });
            configuration.Tenants.Add(new TenantInfo { Id = "tenant-b", DatabaseName = "database-b" });
            _resolver = new SqlTenantDatabaseResolver(_accessor, new TenantManager(Options.Create(configuration)), NullLogger<SqlTenantDatabaseResolver>.Instance);
        }

        [Theory]
        [InlineData("dbo.GetResources", "[database.a]]].[dbo].[GetResources]")]
        [InlineData("CustomQuery_123", "[database.a]]].[dbo].[CustomQuery_123]")]
        [InlineData("[dbo].[Procedure.with]]bracket]", "[database.a]]].[dbo].[Procedure.with]]bracket]")]
        [InlineData("\"dbo\".\"Procedure.with\"\"quote\"", "[database.a]]].[dbo].[Procedure.with\"quote]")]
        [InlineData("[database.a]]].[dbo].[GetResources]", "[database.a]]].[dbo].[GetResources]")]
        public void GivenTenant_WhenPreparingCommandRepeatedly_ThenProcedureIsSafelyQualifiedOnce(string name, string expectedName)
        {
            // Arrange
            SetTenant("tenant-a");
            using var command = new SqlCommand(name) { CommandType = CommandType.StoredProcedure };

            // Act
            _resolver.PrepareCommand(command);
            _resolver.PrepareCommand(command);

            // Assert
            Assert.Equal(expectedName, command.CommandText);
        }

        [Theory]
        [InlineData("[another-database].[dbo].[GetResources]", typeof(InvalidOperationException))]
        [InlineData("dbo.GetResources; SELECT 1", typeof(ArgumentException))]
        [InlineData("server.database.dbo.GetResources", typeof(ArgumentException))]
        public void GivenInvalidOrForeignProcedureName_WhenPreparingTenantCommand_ThenItIsRejected(string name, Type expectedException)
        {
            // Arrange
            SetTenant("tenant-a");
            using var command = new SqlCommand(name) { CommandType = CommandType.StoredProcedure };

            // Act
            var exception = Record.Exception(() => _resolver.PrepareCommand(command));

            // Assert
            Assert.IsType(expectedException, exception);
            Assert.Equal(name, command.CommandText);
        }

        [Fact]
        public void GivenTenant_WhenPreparingTextCommand_ThenSqlAndParametersAreUnchanged()
        {
            // Arrange
            SetTenant("tenant-a");
            using var command = new SqlCommand("SELECT @Text") { CommandType = CommandType.Text };
            command.Parameters.AddWithValue("@Text", "EXEC dbo.GetResources");

            // Act
            _resolver.PrepareCommand(command);

            // Assert
            Assert.Equal("SELECT @Text", command.CommandText);
            Assert.Equal("EXEC dbo.GetResources", command.Parameters["@Text"].Value);
        }

        [Fact]
        public void GivenChangingRequestContext_WhenResolvingProcedureNames_ThenNoTenantStateLeaks()
        {
            // Arrange
            SetTenant("tenant-a");

            // Act
            string first = _resolver.GetStoredProcedureName("dbo.GetResources");
            SetTenant("tenant-b");
            string second = _resolver.GetStoredProcedureName("dbo.GetResources");
            _accessor.RequestContext = null;
            string background = _resolver.GetStoredProcedureName("dbo.GetResources");
            SetTenant(null);
            string independent = _resolver.GetStoredProcedureName("dbo.GetResources");

            // Assert
            Assert.Equal("[database.a]]].[dbo].[GetResources]", first);
            Assert.Equal("[database-b].[dbo].[GetResources]", second);
            Assert.Equal("dbo.GetResources", background);
            Assert.Equal("dbo.GetResources", independent);
        }

        [Theory]
        [InlineData("unknown")]
        [InlineData("")]
        public async Task GivenUnknownTenant_WhenExecutingProcedure_ThenItFailsBeforeOpeningConnection(string tenantId)
        {
            // Arrange
            SetTenant(tenantId);
            var connectionBuilder = Substitute.For<ISqlConnectionBuilder>();
            var service = CreateRetryService(connectionBuilder);
            using var command = new SqlCommand("dbo.GetResources") { CommandType = CommandType.StoredProcedure };

            // Act
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteSql(
                command,
                (_, _) => Task.CompletedTask,
                NullLogger.Instance,
                null,
                CancellationToken.None));

            // Assert
            Assert.Contains("unsupported tenant", exception.Message, StringComparison.Ordinal);
            Assert.Equal("dbo.GetResources", command.CommandText);
            await connectionBuilder.DidNotReceiveWithAnyArgs().GetSqlConnectionAsync(default(bool), default(string));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("unused-tenant")]
        public async Task GivenNoConfiguredTenants_WhenExecutingProcedure_ThenLegacyNameReachesConnectionBuilder(string tenantId)
        {
            // Arrange
            SetTenant(tenantId);
            var resolver = new SqlTenantDatabaseResolver(
                _accessor,
                new TenantManager(Options.Create(new TenantConfiguration())),
                NullLogger<SqlTenantDatabaseResolver>.Instance);
            var builder = Substitute.For<ISqlConnectionBuilder>();
            var stop = new OperationCanceledException("Stop before opening the connection.");
            builder.GetSqlConnectionAsync(Arg.Any<bool>(), Arg.Any<string>()).Returns(Task.FromException<SqlConnection>(stop));
            var service = new SqlRetryService(
                builder,
                Options.Create(new SqlServerDataStoreConfiguration()),
                Options.Create(new SqlRetryServiceOptions { MaxRetries = 1 }),
                new SqlRetryServiceDelegateOptions(),
                Options.Create(new CoreFeatureConfiguration()),
                resolver);
            using var command = new SqlCommand("dbo.GetResources") { CommandType = CommandType.StoredProcedure };

            // Act
            var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => service.ExecuteSql(
                command,
                (_, _) => Task.CompletedTask,
                NullLogger.Instance,
                null,
                CancellationToken.None));

            // Assert
            Assert.Same(stop, exception);
            Assert.Null(resolver.GetDatabaseName());
            Assert.Equal("dbo.GetResources", command.CommandText);
        }

        [Fact]
        public async Task GivenRetriedProcedure_WhenConnectionFails_ThenQualifiedNameRemainsStable()
        {
            // Arrange
            SetTenant("tenant-a");
            var builder = Substitute.For<ISqlConnectionBuilder>();
            var stop = new OperationCanceledException("Stop after the retry.");
            int attempts = 0;
            builder.GetSqlConnectionAsync(Arg.Any<bool>(), Arg.Any<string>()).Returns(_ =>
            {
                attempts++;
                return Task.FromException<SqlConnection>(attempts == 1 ? new TimeoutException("Retry connection.") : stop);
            });
            var service = new SqlRetryService(
                builder,
                Options.Create(new SqlServerDataStoreConfiguration()),
                Options.Create(new SqlRetryServiceOptions { MaxRetries = 2, RetryMillisecondsDelay = 1 }),
                new SqlRetryServiceDelegateOptions { CustomIsExceptionRetriable = exception => exception is TimeoutException },
                Options.Create(new CoreFeatureConfiguration()),
                _resolver);
            using var command = new SqlCommand("dbo.GetResources") { CommandType = CommandType.StoredProcedure };

            // Act
            var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => service.ExecuteSql(
                command,
                (_, _) => Task.CompletedTask,
                NullLogger.Instance,
                null,
                CancellationToken.None));

            // Assert
            Assert.Same(stop, exception);
            Assert.Equal(2, attempts);
            Assert.Equal("[database.a]]].[dbo].[GetResources]", command.CommandText);
        }

        [Theory]
        [InlineData("nonquery")]
        [InlineData("scalar")]
        [InlineData("reader")]
        [InlineData("multi-result")]
        public async Task GivenTenant_WhenUsingCommandExecutionHelpers_ThenProcedureIsQualified(string execution)
        {
            // Arrange
            SetTenant("tenant-a");
            var builder = Substitute.For<ISqlConnectionBuilder>();
            var stop = new OperationCanceledException("Stop before opening the connection.");
            builder.GetSqlConnectionAsync(Arg.Any<bool>(), Arg.Any<string>()).Returns(Task.FromException<SqlConnection>(stop));
            var service = CreateRetryService(builder);
            using var command = new SqlCommand("dbo.GetResources") { CommandType = CommandType.StoredProcedure };

            // Act
            var exception = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            {
                switch (execution)
                {
                    case "nonquery":
                        await command.ExecuteNonQueryAsync(service, NullLogger.Instance, CancellationToken.None);
                        break;
                    case "scalar":
                        await command.ExecuteScalarAsync(service, NullLogger.Instance, CancellationToken.None);
                        break;
                    case "reader":
                        await command.ExecuteReaderAsync(service, reader => reader.GetInt64(0), NullLogger.Instance, CancellationToken.None);
                        break;
                    case "multi-result":
                        await command.ExecuteMultiResultReaderAsync<long>(service, [reader => reader.GetInt64(0)], NullLogger.Instance, CancellationToken.None);
                        break;
                }
            });

            // Assert
            Assert.Same(stop, exception);
            Assert.Equal("[database.a]]].[dbo].[GetResources]", command.CommandText);
        }

        [Theory]
        [InlineData(null, "tenant-a", "[database.a]]].[dbo].[LogEvent]")]
        [InlineData("Server=log-server;Database=logs", "tenant-a", "dbo.LogEvent")]
        [InlineData("Server=log-server;Database=logs", "unknown", "dbo.LogEvent")]
        public void GivenEventLogDestination_WhenCreatingCommand_ThenOnlyDataConnectionUsesTenantDatabase(string connectionString, string tenantId, string expectedName)
        {
            // Arrange
            SetTenant(tenantId);
            var service = CreateRetryService(Substitute.For<ISqlConnectionBuilder>());
            var method = typeof(SqlRetryService).GetMethod("CreateEventLogCommand", BindingFlags.Instance | BindingFlags.NonPublic);

            // Act
            using var command = (SqlCommand)method.Invoke(service, new object[] { connectionString });

            // Assert
            Assert.Equal(CommandType.StoredProcedure, command.CommandType);
            Assert.Equal(expectedName, command.CommandText);
        }

        private SqlRetryService CreateRetryService(ISqlConnectionBuilder builder) => new(
            builder,
            Options.Create(new SqlServerDataStoreConfiguration()),
            Options.Create(new SqlRetryServiceOptions { MaxRetries = 1 }),
            new SqlRetryServiceDelegateOptions(),
            Options.Create(new CoreFeatureConfiguration()),
            _resolver);

        private void SetTenant(string id)
        {
            var context = Substitute.For<IFhirRequestContext>();
            context.TenantId.Returns(id);
            _accessor.RequestContext = context;
        }
    }
}
