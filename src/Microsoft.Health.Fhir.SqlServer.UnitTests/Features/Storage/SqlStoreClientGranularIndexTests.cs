// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlClient.Server;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Storage
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SqlStoreClientGranularIndexTests
    {
        [Fact]
        public void GivenGeneratedRows_WhenReadingKeys_ThenCombinationsAreDistinctAndRowsRemainReadable()
        {
            // Arrange
            using var command = CreateWriteCommand(10, 10, 11);

            // Act
            var first = SqlStoreClient.GetSearchParameterIndexKeys(command.Parameters);
            var second = SqlStoreClient.GetSearchParameterIndexKeys(command.Parameters);

            // Assert
            Assert.Equal(2, first.Count);
            Assert.Contains(("TokenSearchParam", (short)1, (short)10), first);
            Assert.Contains(("TokenSearchParam", (short)1, (short)11), first);
            Assert.True(first.SetEquals(second));
        }

        [Theory]
        [InlineData("dbo.ReferenceSearchParamList", "ReferenceSearchParam")]
        [InlineData("dbo.TokenNumberNumberCompositeSearchParamList", "TokenNumberNumberCompositeSearchParam")]
        [InlineData("dbo.VectorSearchParamList", "VectorSearchParam")]
        [InlineData("dbo.TokenTextList", "TokenText")]
        [InlineData("dbo.FutureSearchParamList", "FutureSearchParam")]
        [InlineData("dbo.SearchParamList", null)]
        [InlineData("dbo.ResourceList", null)]
        [InlineData("dbo.TokenTextListExtra", null)]
        public void GivenTvpTypeName_WhenReadingKeys_ThenOnlyMatchingNamesAreMapped(string typeName, string expectedTableName)
        {
            // Arrange
            using var command = new SqlCommand();
            var record = new SqlDataRecord(
                new SqlMetaData("ResourceTypeId", SqlDbType.SmallInt),
                new SqlMetaData("ResourceSurrogateId", SqlDbType.BigInt),
                new SqlMetaData("SearchParamId", SqlDbType.SmallInt));
            record.SetInt16(0, 1);
            record.SetInt64(1, 123);
            record.SetInt16(2, 10);
            command.Parameters.Add(new SqlParameter("@Rows", SqlDbType.Structured) { TypeName = typeName, Value = new[] { record } });

            // Act
            var keys = SqlStoreClient.GetSearchParameterIndexKeys(command.Parameters);

            // Assert
            if (expectedTableName == null)
            {
                Assert.Empty(keys);
            }
            else
            {
                Assert.Equal((expectedTableName, (short)1, (short)10), Assert.Single(keys));
            }
        }

        [Fact]
        public async Task GivenCachedCombinations_WhenWritingAgain_ThenOnlyNewCombinationsReachSql()
        {
            // Arrange
            var retryService = Substitute.For<ISqlRetryService>();
            var client = CreateClient(retryService);
            using var first = CreateWriteCommand(10, 10);
            using var second = CreateWriteCommand(10, 11);

            // Act
            await client.CreateGranularSecondaryIndexesAsync(first.Parameters, CancellationToken.None);
            await client.CreateGranularSecondaryIndexesAsync(first.Parameters, CancellationToken.None);
            await client.CreateGranularSecondaryIndexesAsync(second.Parameters, CancellationToken.None);

            // Assert
            var commands = GetExecutedCommands(retryService);
            Assert.Equal(2, commands.Count);
            Assert.All(commands, command => Assert.Equal("dbo.CreateGranularSecondaryIndexes", command.CommandText));
            Assert.All(commands, command => Assert.Equal(3, command.Parameters.Count));
            Assert.Equal("TokenSearchParam", commands[0].Parameters["@TableName"].Value);
            Assert.Equal((short)1, commands[0].Parameters["@ResourceTypeId"].Value);
            Assert.Equal((short)10, commands[0].Parameters["@SearchParamId"].Value);
            Assert.Equal((short)11, commands[1].Parameters["@SearchParamId"].Value);
        }

        [Fact]
        public async Task GivenLaterCombinationFails_WhenRetrying_ThenEarlierSuccessIsCachedAndFailureIsRetried()
        {
            // Arrange
            var retryService = Substitute.For<ISqlRetryService>();
            var fail = true;
            retryService.WhenForAnyArgs(service => service.ExecuteSql(default(SqlCommand), default, default, default, default, default, default, default))
                .Do(call =>
                {
                    var command = call.ArgAt<SqlCommand>(0);
                    if (fail && (short)command.Parameters["@SearchParamId"].Value == 11)
                    {
                        throw new InvalidOperationException("Index creation failed.");
                    }
                });
            var client = CreateClient(retryService);
            using var write = CreateWriteCommand(10, 11);

            // Act
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.CreateGranularSecondaryIndexesAsync(write.Parameters, CancellationToken.None));
            fail = false;
            await client.CreateGranularSecondaryIndexesAsync(write.Parameters, CancellationToken.None);

            // Assert
            var ids = GetExecutedCommands(retryService).Select(command => (short)command.Parameters["@SearchParamId"].Value).ToArray();
            Assert.Equal(new short[] { 10, 11, 11 }, ids);
        }

        [Fact]
        public async Task GivenConcurrentCacheMisses_WhenEnsuringSameCombination_ThenSqlHandlesConcurrencyAndSuccessIsCached()
        {
            // Arrange
            var retryService = Substitute.For<ISqlRetryService>();
            var creation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            retryService.ExecuteSql(default(SqlCommand), default, default, default, default, default, default, default).ReturnsForAnyArgs(creation.Task);
            var client = CreateClient(retryService);
            using var write = CreateWriteCommand(10);

            // Act
            var writes = Enumerable.Range(0, 10).Select(_ => client.CreateGranularSecondaryIndexesAsync(write.Parameters, CancellationToken.None)).ToArray();
            var callsBeforeCompletion = GetExecutedCommands(retryService).Count;
            creation.SetResult();
            await Task.WhenAll(writes);
            await client.CreateGranularSecondaryIndexesAsync(write.Parameters, CancellationToken.None);

            // Assert
            Assert.Equal(10, callsBeforeCompletion);
            Assert.Equal(10, GetExecutedCommands(retryService).Count);
        }

        [Fact]
        public async Task GivenOlderSchema_WhenWriting_ThenGranularIndexProcedureIsNotCalled()
        {
            // Arrange
            var retryService = Substitute.For<ISqlRetryService>();
            var client = CreateClient(retryService, SchemaVersionConstants.GranularSearchParameterIndexes - 1);
            using var write = CreateWriteCommand(10);

            // Act
            await client.CreateGranularSecondaryIndexesAsync(write.Parameters, CancellationToken.None);

            // Assert
            Assert.Empty(GetExecutedCommands(retryService));
        }

        [Fact]
        public async Task GivenEmptyGeneratedRows_WhenWriting_ThenSqlIsNotCalled()
        {
            // Arrange
            var retryService = Substitute.For<ISqlRetryService>();
            var client = CreateClient(retryService);
            using var write = CreateWriteCommand();

            // Act
            await client.CreateGranularSecondaryIndexesAsync(write.Parameters, CancellationToken.None);

            // Assert
            Assert.Empty(GetExecutedCommands(retryService));
        }

        private static SqlStoreClient CreateClient(ISqlRetryService retryService, int version = SchemaVersionConstants.GranularSearchParameterIndexes)
        {
            var schema = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max) { Current = version };
            return new SqlStoreClient(retryService, NullLogger<SqlStoreClient>.Instance, schema);
        }

        private static SqlCommand CreateWriteCommand(params short[] searchParamIds)
        {
            var command = new SqlCommand();
            new TokenSearchParamListTableValuedParameterDefinition("@TokenSearchParams").AddParameter(
                command.Parameters,
                searchParamIds.Select((id, index) => new TokenSearchParamListRow(1, index + 1, id, null, "code", null)));
            return command;
        }

        private static List<SqlCommand> GetExecutedCommands(ISqlRetryService retryService) => retryService.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(ISqlRetryService.ExecuteSql))
            .Select(call => (SqlCommand)call.GetArguments()[0]).ToList();
    }
}
