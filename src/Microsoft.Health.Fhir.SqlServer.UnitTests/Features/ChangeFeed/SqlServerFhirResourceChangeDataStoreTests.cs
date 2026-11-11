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
using Microsoft.Health.Fhir.Core.Features.Partitions;
using Microsoft.Health.Fhir.SqlServer.Features.ChangeFeed;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
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

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.ChangeFeed
{
    /// <summary>
    /// Unit tests to validates input parameters of a GetRecordsAsync function.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class SqlServerFhirResourceChangeDataStoreTests
    {
        private readonly SqlServerFhirResourceChangeDataStore resourceChangeDataStore;
        private const string LocalConnectionString = "server=(local);Integrated Security=true;TrustServerCertificate=True";

        public SqlServerFhirResourceChangeDataStoreTests()
        {
            resourceChangeDataStore = GetResourcChangeDataStoreWithGivenConnectionString(new SqlConnectionStringBuilder(LocalConnectionString) { InitialCatalog = "testDb" }.ToString());
        }

        [Fact]
        public async Task GivenTheStartIdLessThanOne_WhenGetResourceChanges_ThenExceptionShouldBeThrown()
        {
            var expectedStartString = "Value '-1' is not greater than or equal to limit '1'. (Parameter 'startId')";
            var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await resourceChangeDataStore.GetRecordsAsync(-1, 200, CancellationToken.None));
            Assert.StartsWith(expectedStartString, exception.Message);
        }

        [Fact]
        public async Task GivenThePageSizeLessThanOne_WhenGetResourceChanges_ThenExceptionShouldBeThrown()
        {
            var expectedStartString = "Value '-1' is not greater than or equal to limit '1'. (Parameter 'pageSize')";
            var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await resourceChangeDataStore.GetRecordsAsync(1, -1, CancellationToken.None));
            Assert.StartsWith(expectedStartString, exception.Message);
        }

        [Fact]
        public async Task GivenThePageSizeLessThanOne_WhenGetResourceChanges_ThenArgumentOutOfRangeExceptionShouldBeThrown()
        {
            try
            {
                await resourceChangeDataStore.GetRecordsAsync(1, -1, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Assert.Equal(nameof(ArgumentOutOfRangeException), ex.GetType().Name);
            }
        }

        [Fact]
        public async Task GivenEmptyConnectionString_WhenGetResourceChanges_ThenInvalidOperationExceptionShouldBeThrown()
        {
            try
            {
                var resourceChangeDataStore = GetResourcChangeDataStoreWithGivenConnectionString(string.Empty);

                await resourceChangeDataStore.GetRecordsAsync(1, 200, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Assert.Equal(nameof(ArgumentException), ex.GetType().Name);
            }
        }

        [Fact]
        public async Task GivenOperationCanceled_WhenGetResourceChanges_ThenTaskCanceledExceptionShouldBeThrown()
        {
            var source = new CancellationTokenSource();
            var token = source.Token;
            source.Cancel();

            var resourceChangeDataStore = GetResourcChangeDataStoreWithGivenConnectionString(new SqlConnectionStringBuilder(LocalConnectionString) { InitialCatalog = "testDb" }.ToString());

            try
            {
                await resourceChangeDataStore.GetRecordsAsync(1, 200, token);
            }
            catch (Exception ex)
            {
                Assert.Equal(nameof(TaskCanceledException), ex.GetType().Name);
            }
        }

        [Theory]
        [InlineData(19, "FetchResourceChanges")]
        [InlineData(20, "FetchResourceChanges_2")]
        [InlineData(24, "FetchResourceChanges_3")]
        public void GivenPartition_WhenPopulatingChangeFeedCommand_ThenSchemaSpecificProcedureUsesPartitionDatabase(int schemaVersion, string procedureName)
        {
            // Arrange
            var context = Substitute.For<IFhirRequestContext>();
            context.PartitionId.Returns("partition-a");
            var configuration = new PartitionConfiguration();
            configuration.Partitions.Add(new PartitionInfo { Id = "partition-a", DatabaseName = "database-a" });
            var resolver = new SqlPartitionDatabaseResolver(
                new FhirRequestContextAccessor { RequestContext = context },
                new PartitionManager(Options.Create(configuration)),
                NullLogger<SqlPartitionDatabaseResolver>.Instance);
            var store = GetResourcChangeDataStoreWithGivenConnectionString(LocalConnectionString, resolver, schemaVersion);
            using var command = new SqlCommand();
            using var wrapper = new SqlCommandWrapper(command);

            // Act
            typeof(SqlServerFhirResourceChangeDataStore)
                .GetMethod("PopulateFetchResourceChangesCommand", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(store, [wrapper, 1L, DateTime.UnixEpoch, (short)10]);

            // Assert
            Assert.Equal("[database-a].[dbo].[" + procedureName + "]", command.CommandText);
            Assert.Equal(CommandType.StoredProcedure, command.CommandType);
            Assert.Equal(1L, command.Parameters["@startId"].Value);
            Assert.Equal((short)10, command.Parameters["@pageSize"].Value);
        }

        public SqlServerFhirResourceChangeDataStore GetResourcChangeDataStoreWithGivenConnectionString(string connectionString, SqlPartitionDatabaseResolver partitionDatabaseResolver = null, int schemaVersion = SchemaVersionConstants.Max)
        {
            var schemaOptions = new SqlServerSchemaOptions { AutomaticUpdatesEnabled = true };
            var config = Options.Create(new SqlServerDataStoreConfiguration { ConnectionString = connectionString, Initialize = true, SchemaOptions = schemaOptions, StatementTimeout = TimeSpan.FromMinutes(10) });
            var sqlRetryLogicBaseProvider = SqlConfigurableRetryFactory.CreateNoneRetryProvider();
            var sqlConnectionBuilder = new DefaultSqlConnectionBuilder(config, sqlRetryLogicBaseProvider);
            var sqlConnectionWrapperFactory = new SqlConnectionWrapperFactory(new SqlTransactionHandler(), sqlConnectionBuilder, sqlRetryLogicBaseProvider, config);

            var schemaInformation = new SchemaInformation(Math.Min(schemaVersion, SchemaVersionConstants.Min), SchemaVersionConstants.Max);
            schemaInformation.Current = schemaVersion;
            partitionDatabaseResolver ??= new SqlPartitionDatabaseResolver(
                new FhirRequestContextAccessor(),
                new PartitionManager(Options.Create(new PartitionConfiguration())),
                NullLogger<SqlPartitionDatabaseResolver>.Instance);

            return new SqlServerFhirResourceChangeDataStore(
                sqlConnectionWrapperFactory,
                NullLogger<SqlServerFhirResourceChangeDataStore>.Instance,
                schemaInformation,
                partitionDatabaseResolver);
        }
    }
}
