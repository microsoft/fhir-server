// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.SqlServer.Features.ChangeFeed;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Configs;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.ChangeFeed
{
    public class SqlServerFhirResourceChangeDataStoreTests
    {
        private SqlServerFhirResourceChangeDataStore resourceChangeDataStore;

        public SqlServerFhirResourceChangeDataStoreTests()
        {
            var config = new SqlServerDataStoreConfiguration { ConnectionString = string.Empty };
            var connectionStringProvider = new DefaultSqlConnectionStringProvider(config);
            var connectionFactory = new DefaultSqlConnectionFactory(connectionStringProvider);
            resourceChangeDataStore = new SqlServerFhirResourceChangeDataStore(connectionFactory, NullLogger<SqlServerFhirResourceChangeDataStore>.Instance);
        }

        [Fact]
        public async Task GivenTheStartIdLessThanZero_ExceptionShouldBeThrown()
        {
            var expectedStartString = "Value '-1' is not greater than or equal to limit '0'. (Parameter 'startId')";
            var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await resourceChangeDataStore.GetRecordsAsync(-1, 200, CancellationToken.None));
            Assert.StartsWith(expectedStartString, exception.Message);
        }

        [Fact]
        public async Task GivenThePageSizeLessThanZero_ExceptionShouldBeThrown()
        {
            var expectedStartString = "Value '-1' is not greater than or equal to limit '0'. (Parameter 'pageSize')";
            var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await resourceChangeDataStore.GetRecordsAsync(0, -1, CancellationToken.None));
            Assert.StartsWith(expectedStartString, exception.Message);
        }
    }
}
