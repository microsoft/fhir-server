// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.SqlServer.Features.ChangeFeed;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Configs;
using Microsoft.Health.SqlServer.Features.Schema;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.ChangeFeed
{
    /// <summary>
    /// Unit tests to validates input parameters of a GetRecordsAsync function.
    /// </summary>
    public class SqlServerFhirResourceChangeDataStoreTests
    {
        private readonly SqlServerFhirResourceChangeDataStore resourceChangeDataStore;

        public SqlServerFhirResourceChangeDataStoreTests()
        {
            var config = Options.Create(new SqlServerDataStoreConfiguration { ConnectionString = string.Empty });
            var connectionStringProvider = new DefaultSqlConnectionStringProvider(config);
            var connectionFactory = new DefaultSqlConnectionFactory(connectionStringProvider);
            var schemaInformation = new SchemaInformation((int)SchemaVersion.V15, SchemaVersionConstants.Max);
            schemaInformation.Current = SchemaVersionConstants.Max;
            resourceChangeDataStore = new SqlServerFhirResourceChangeDataStore(connectionFactory, NullLogger<SqlServerFhirResourceChangeDataStore>.Instance, schemaInformation);
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
                await resourceChangeDataStore.GetRecordsAsync(1, 200, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Assert.Equal(nameof(InvalidOperationException), ex.GetType().Name);
            }
        }
    }
}
