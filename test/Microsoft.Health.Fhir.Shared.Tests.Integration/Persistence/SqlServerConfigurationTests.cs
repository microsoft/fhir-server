// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.SqlServer.Configuration;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.SqlServer.Features.Client;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    [FhirStorageTestsFixtureArgumentSets(DataStore.SqlServer)]
    public class SqlServerConfigurationTests : IClassFixture<SqlServerFhirStorageTestsFixture>
    {
        private SqlServerFhirStorageTestsFixture _fixture;
        private readonly ISqlServerConfiguration _sqlServerConfiguration = Substitute.For<ISqlServerConfiguration>();

        public SqlServerConfigurationTests(SqlServerFhirStorageTestsFixture fixture)
        {
            _fixture = fixture;
        }

        [Theory]
        [InlineData(1, 5)]
        [InlineData(0, 5)]
        [InlineData(0, 0)]
        [InlineData(15, 0)]
        [InlineData(20, 25)]
        [InlineData(45, 50)]
        [InlineData(70, 75)]
        public async Task Given_VariousCommandTimeoutSettings_TheyAllSucceed(int longRunningSqlDelayTimeout, int sqlCommandTimeout)
        {
            // regarding the Sql CommandTimeout
            // A value of 0 indicates no limit (an attempt to execute a command will wait indefinitely).
            // https://docs.microsoft.com/en-us/dotnet/api/system.data.sqlclient.sqlcommand.commandtimeout?view=dotnet-plat-ext-6.0

            int selectValue = 1;
            DateTime now = DateTime.Now;
            string waitForDelay = new DateTime(now.Year, now.Month, now.Day).AddSeconds(longRunningSqlDelayTimeout).ToString("HH:mm:ss", CultureInfo.InvariantCulture);

            _sqlServerConfiguration.GetCommandTimeout().Returns(sqlCommandTimeout);
            using (SqlConnectionWrapper connectionWrapper = await _fixture.SqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None))
            using (SqlCommandWrapper sqlCommandWrapper = connectionWrapper.CreateSqlCommand())
            {
                sqlCommandWrapper.CommandTimeout = sqlCommandTimeout;
                sqlCommandWrapper.CommandText = $@"
                            WAITFOR DELAY '{waitForDelay}';
                            SELECT TOP 1 {selectValue} FROM ResourceType";

                int selectResult = (int)await sqlCommandWrapper.ExecuteScalarAsync(CancellationToken.None);
                Assert.Equal(selectValue, selectResult);
            }
        }
    }
}
