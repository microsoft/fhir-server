// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.SqlServer.Features.ChangeFeed;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Features.Schema;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.ChangeFeed
{
    /// <summary>
    /// Unit tests to validates input parameters of a GetRecordsAsync function.
    /// </summary>
    public class SqlServerFhirResourceChangeDataStoreTests
    {
        private readonly SqlServerFhirResourceChangeDataStore resourceChangeDataStore;

        private ISqlConnectionFactory connectionFactory;

        public SqlServerFhirResourceChangeDataStoreTests()
        {
            connectionFactory = Substitute.For<ISqlConnectionFactory>();
            var schemaInformation = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max);
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
            connectionFactory.GetSqlConnectionAsync(default, default).Returns(new SqlConnection(string.Empty));

            try
            {
                await resourceChangeDataStore.GetRecordsAsync(1, 200, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Assert.Equal(nameof(InvalidOperationException), ex.GetType().Name);
            }
        }

        [Fact]
        public async Task GivenOperationCanceled_WhenGetResourceChanges_ThenTaskCanceledExceptionShouldBeThrown()
        {
            var source = new CancellationTokenSource();
            var token = source.Token;
            source.Cancel();

            connectionFactory.GetSqlConnectionAsync(default, token).Returns(new SqlConnection());

            try
            {
                await resourceChangeDataStore.GetRecordsAsync(1, 200, token);
            }
            catch (Exception ex)
            {
                Assert.Equal(nameof(TaskCanceledException), ex.GetType().Name);
            }
        }
    }
}
