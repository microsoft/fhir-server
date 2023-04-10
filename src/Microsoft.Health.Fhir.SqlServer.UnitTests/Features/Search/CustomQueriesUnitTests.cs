// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Data;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class CustomQueriesUnitTests
    {
        [Fact]
        public void GivenCustomQueryClass_WithGivenWaitTime_QueryToDBWillOccurOnlyAfterWaitPeriod()
        {
            // set wait time to 2 seconds;
            CustomQueries.WaitTime = 1;

            var connection = Substitute.For<IDbConnection>();
            var command = Substitute.For<IDbCommand>();
            var reader = Substitute.For<IDataReader>();
            reader.Read().Returns(false);
            command.ExecuteReader().Returns(reader);
            connection.CreateCommand().Returns(command);

            var logger = NullLogger<SqlServerSearchService>.Instance;

            CustomQueries.CheckQueryHash(connection, "hash", logger);
            CustomQueries.CheckQueryHash(connection, "hash", logger);
            CustomQueries.CheckQueryHash(connection, "hash", logger);
            Task.Delay(1100).Wait();

            CustomQueries.CheckQueryHash(connection, "hash", logger);

            connection.Received(2).CreateCommand();
        }

        [Fact]
        public void GivenCustomQueryClass_WithGivenSprocName_DictionaryWillPopulateCorrectly()
        {
            // set wait time to 1 second
            CustomQueries.WaitTime = 1;

            // Wait long enough to ensure we query the DB
            Task.Delay(1100).Wait();

            var connection = Substitute.For<IDbConnection>();
            var command = Substitute.For<IDbCommand>();
            var reader = Substitute.For<IDataReader>();
            reader.Read().Returns(x => true, x => true, x => true, x => true, x => true, x => false);
            reader.GetString(0).Returns(x => "CustomQuery_hash1", x => throw new System.Exception(), x => "CustomQuery_hash1", x => "badData", x => "CustomQuery_hash2");
            command.ExecuteReader().Returns(reader);
            connection.CreateCommand().Returns(command);

            var logger = NullLogger<SqlServerSearchService>.Instance;

            Assert.Null(CustomQueries.CheckQueryHash(connection, "hash2", logger));

            Assert.Equal("CustomQuery_hash1", CustomQueries.CheckQueryHash(connection, "hash1", logger));

            // set wait time to 60 seconds to make sure we don't query the DB after success
            CustomQueries.WaitTime = 60;

            Assert.Equal("CustomQuery_hash2", CustomQueries.CheckQueryHash(connection, "hash2", logger));
        }
    }
}
