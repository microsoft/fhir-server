// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.SqlServer.Features.ChangeFeed;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Configs;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Storage;
using Microsoft.Health.Test.Utilities;
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
            var resourceChangeDataStore = GetResourcChangeDataStoreWithGivenConnectionString(string.Empty);

            try
            {
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

        public SqlServerFhirResourceChangeDataStore GetResourcChangeDataStoreWithGivenConnectionString(string connectionString)
        {
            var schemaOptions = new SqlServerSchemaOptions { AutomaticUpdatesEnabled = true };
            var config = Options.Create(new SqlServerDataStoreConfiguration { ConnectionString = connectionString, Initialize = true, SchemaOptions = schemaOptions, StatementTimeout = TimeSpan.FromMinutes(10) });
            var sqlRetryLogicBaseProvider = SqlConfigurableRetryFactory.CreateNoneRetryProvider();
            var sqlConnectionBuilder = new DefaultSqlConnectionBuilder(config, sqlRetryLogicBaseProvider);
            var sqlConnectionWrapperFactory = new SqlConnectionWrapperFactory(new SqlTransactionHandler(), sqlConnectionBuilder, sqlRetryLogicBaseProvider, config);

            var schemaInformation = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max);
            schemaInformation.Current = SchemaVersionConstants.Max;

            return new SqlServerFhirResourceChangeDataStore(sqlConnectionWrapperFactory, NullLogger<SqlServerFhirResourceChangeDataStore>.Instance, schemaInformation);
        }
    }
}
