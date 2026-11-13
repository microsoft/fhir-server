// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [CollectionDefinition(nameof(CustomQueriesUnitTests), DisableParallelization = true)]
    [Collection(nameof(CustomQueriesUnitTests))]
    public class CustomQueriesUnitTests : IDisposable
    {
        public CustomQueriesUnitTests()
        {
            CustomQueries.ResetForTests();
        }

        public void Dispose()
        {
            CustomQueries.ResetForTests();
        }

        [Fact]
        public void GivenCustomQueryClass_WithGivenWaitTime_QueryToDBWillOccurOnlyAfterWaitPeriod()
        {
            var timeProvider = new FakeTimeProvider();
            using var clock = Mock.Property(() => ClockResolver.TimeProvider, timeProvider);
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
            connection.Received(1).CreateCommand();

            timeProvider.Advance(TimeSpan.FromSeconds(1));
            CustomQueries.CheckQueryHash(connection, "hash", logger);
            connection.Received(1).CreateCommand();

            timeProvider.Advance(TimeSpan.FromTicks(1));
            CustomQueries.CheckQueryHash(connection, "hash", logger);

            connection.Received(2).CreateCommand();
        }

        [Fact]
        public void GivenCustomQueryClass_WithGivenSprocName_DictionaryWillPopulateCorrectly()
        {
            var timeProvider = new FakeTimeProvider();
            using var clock = Mock.Property(() => ClockResolver.TimeProvider, timeProvider);
            CustomQueries.WaitTime = 1;

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

            CustomQueries.WaitTime = 60;
            timeProvider.Advance(TimeSpan.FromSeconds(59));

            Assert.Equal("CustomQuery_hash2", CustomQueries.CheckQueryHash(connection, "hash2", logger));
            connection.Received(2).CreateCommand();
        }
    }
}
