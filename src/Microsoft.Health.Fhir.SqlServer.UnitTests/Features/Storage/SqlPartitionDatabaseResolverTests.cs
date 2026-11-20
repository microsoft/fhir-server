// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.SqlServer.Features.Partitions;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.SqlServer.Registration;
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
    public class SqlPartitionDatabaseResolverTests : IDisposable
    {
        private readonly FhirRequestContextAccessor _accessor = new FhirRequestContextAccessor();
        private readonly SqlPartitionDatabaseResolver _resolver;
        private readonly ServiceProvider _provider;
        private readonly IServiceScope _scope;
        private readonly IHttpContextAccessor _http;

        public SqlPartitionDatabaseResolverTests()
        {
            var configuration = new PartitionConfiguration();
            configuration.Partitions.Add(new PartitionInfo { Id = "partition-a", DatabaseName = "database.a]" });
            configuration.Partitions.Add(new PartitionInfo { Id = "partition-b", DatabaseName = "database-b" });
            _provider = CreateProvider(configuration);
            _scope = _provider.CreateScope();
            _http = _provider.GetRequiredService<IHttpContextAccessor>();
            _resolver = _provider.GetRequiredService<SqlPartitionDatabaseResolver>();
        }

        public void Dispose()
        {
            _http.HttpContext = null;
            _accessor.RequestContext = null;
            _scope.Dispose();
            _provider.Dispose();
        }

        [Theory]
        [InlineData("dbo.GetResources", "[database.a]]].[dbo].[GetResources]")]
        [InlineData("CustomQuery_123", "[database.a]]].[dbo].[CustomQuery_123]")]
        [InlineData("[dbo].[Procedure.with]]bracket]", "[database.a]]].[dbo].[Procedure.with]]bracket]")]
        [InlineData("\"dbo\".\"Procedure.with\"\"quote\"", "[database.a]]].[dbo].[Procedure.with\"quote]")]
        [InlineData("[database.a]]].[dbo].[GetResources]", "[database.a]]].[dbo].[GetResources]")]
        public void GivenPartition_WhenPreparingCommandRepeatedly_ThenProcedureIsSafelyQualifiedOnce(string name, string expectedName)
        {
            // Arrange
            SetPartition("partition-a");
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
        public void GivenInvalidOrForeignProcedureName_WhenPreparingPartitionCommand_ThenItIsRejected(string name, Type expectedException)
        {
            // Arrange
            SetPartition("partition-a");
            using var command = new SqlCommand(name) { CommandType = CommandType.StoredProcedure };

            // Act
            var exception = Record.Exception(() => _resolver.PrepareCommand(command));

            // Assert
            Assert.IsType(expectedException, exception);
            Assert.Equal(name, command.CommandText);
        }

        [Fact]
        public void GivenPartition_WhenPreparingTextCommand_ThenSqlAndParametersAreUnchanged()
        {
            // Arrange
            SetPartition("partition-a");
            using var command = new SqlCommand("SELECT @Text") { CommandType = CommandType.Text };
            command.Parameters.AddWithValue("@Text", "EXEC dbo.GetResources");

            // Act
            _resolver.PrepareCommand(command);

            // Assert
            Assert.Equal("SELECT @Text", command.CommandText);
            Assert.Equal("EXEC dbo.GetResources", command.Parameters["@Text"].Value);
        }

        [Theory]
        [InlineData("unknown")]
        [InlineData("")]
        public async Task GivenUnknownPartition_WhenExecutingProcedure_ThenItFailsBeforeOpeningConnection(string partitionId)
        {
            // Arrange
            SetPartition(partitionId);
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
            Assert.Contains("unsupported partition", exception.Message, StringComparison.Ordinal);
            Assert.Equal("dbo.GetResources", command.CommandText);
            await connectionBuilder.DidNotReceiveWithAnyArgs().GetSqlConnectionAsync(default(bool), default(string));
        }

        [Fact]
        public void GivenSeparateRequestScopes_WhenPreparingCommands_ThenEachScopeKeepsItsOwnDatabaseTarget()
        {
            // Arrange: SQL can run before authentication selects a partition.
            using var secondScope = _provider.CreateScope();
            SetPartition(null);
            Assert.Equal("dbo.GetResources", _resolver.GetStoredProcedureName("dbo.GetResources"));

            // Act and assert.
            SetPartition("partition-a");
            Assert.Equal("[database.a]]].[dbo].[GetResources]", _resolver.GetStoredProcedureName("dbo.GetResources"));
            SetPartition("partition-b");
            Assert.Equal("[database.a]]].[dbo].[MergeResources]", _resolver.GetStoredProcedureName("dbo.MergeResources"));

            _http.HttpContext = new DefaultHttpContext { RequestServices = secondScope.ServiceProvider };
            Assert.Equal("[database-b].[dbo].[GetResources]", _resolver.GetStoredProcedureName("dbo.GetResources"));

            _http.HttpContext = null;
            Assert.Equal("dbo.GetResources", _resolver.GetStoredProcedureName("dbo.GetResources"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("unused-partition")]
        public async Task GivenNoConfiguredPartitions_WhenExecutingProcedure_ThenLegacyNameReachesConnectionBuilder(string partitionId)
        {
            // Arrange
            SetPartition(partitionId);
            using var provider = CreateProvider(new PartitionConfiguration());
            using var scope = provider.CreateScope();
            _http.HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            var resolver = provider.GetRequiredService<SqlPartitionDatabaseResolver>();
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
            SetPartition("partition-a");
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
        public async Task GivenPartition_WhenUsingCommandExecutionHelpers_ThenProcedureIsQualified(string execution)
        {
            // Arrange
            SetPartition("partition-a");
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
        [InlineData(null, "partition-a", "[database.a]]].[dbo].[LogEvent]")]
        [InlineData("Server=log-server;Database=logs", "partition-a", "dbo.LogEvent")]
        [InlineData("Server=log-server;Database=logs", "unknown", "dbo.LogEvent")]
        public void GivenEventLogDestination_WhenCreatingCommand_ThenOnlyDataConnectionUsesPartitionDatabase(string connectionString, string partitionId, string expectedName)
        {
            // Arrange
            SetPartition(partitionId);
            var service = CreateRetryService(Substitute.For<ISqlConnectionBuilder>());

            // Act
            using var command = service.CreateEventLogCommand(connectionString, databaseTarget: null);

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

        private void SetPartition(string id)
        {
            _http.HttpContext ??= new DefaultHttpContext { RequestServices = _scope.ServiceProvider };
            var context = Substitute.For<IFhirRequestContext>();
            context.Partition.Returns(new PartitionInfo { Id = id });
            _accessor.RequestContext = context;
        }

        private ServiceProvider CreateProvider(PartitionConfiguration configuration)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<RequestContextAccessor<IFhirRequestContext>>(_accessor);
            services.AddSingleton(new PartitionManager(Options.Create(configuration)));
            services.AddSqlPartitionDatabaseSelection();
            return services.BuildServiceProvider(validateScopes: true);
        }
    }
}
