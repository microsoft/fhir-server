// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Storage
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class SqlStoreClientTests
    {
        [Fact]
        public async Task GivenTransactionDefinitionAndSupportedSchema_WhenBeginningTransaction_ThenDefinitionIsSentToSql()
        {
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            SqlCommand capturedCommand = null;
            sqlRetryService
                .ExecuteSql(
                    Arg.Do<SqlCommand>(command =>
                    {
                        capturedCommand = command;
                        command.Parameters["@TransactionId"].Value = 123L;
                        command.Parameters["@SequenceRangeFirstValue"].Value = 1;
                    }),
                    Arg.Any<Func<SqlCommand, CancellationToken, Task>>(),
                    Arg.Any<ILogger>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>(),
                    Arg.Any<bool>(),
                    Arg.Any<bool>(),
                    Arg.Any<string>())
                .Returns(Task.CompletedTask);

            var client = CreateClient(sqlRetryService, SchemaVersionConstants.TransactionRequestContext);

            await client.MergeResourcesBeginTransactionAsync(1, CancellationToken.None, definition: "correlationId=correlation-123");

            Assert.NotNull(capturedCommand);
            Assert.Equal("dbo.MergeResourcesBeginTransaction", capturedCommand.CommandText);
            Assert.Equal("correlationId=correlation-123", capturedCommand.Parameters["@Definition"].Value);
        }

        [Fact]
        public async Task GivenTransactionDefinitionAndOlderSchema_WhenBeginningTransaction_ThenDefinitionIsNotSentToSql()
        {
            var sqlRetryService = Substitute.For<ISqlRetryService>();
            SqlCommand capturedCommand = null;
            sqlRetryService
                .ExecuteSql(
                    Arg.Do<SqlCommand>(command =>
                    {
                        capturedCommand = command;
                        command.Parameters["@TransactionId"].Value = 123L;
                        command.Parameters["@SequenceRangeFirstValue"].Value = 1;
                    }),
                    Arg.Any<Func<SqlCommand, CancellationToken, Task>>(),
                    Arg.Any<ILogger>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>(),
                    Arg.Any<bool>(),
                    Arg.Any<bool>(),
                    Arg.Any<string>())
                .Returns(Task.CompletedTask);

            var client = CreateClient(sqlRetryService, SchemaVersionConstants.TransactionRequestContext - 1);

            await client.MergeResourcesBeginTransactionAsync(1, CancellationToken.None, definition: "correlationId=correlation-123");

            Assert.NotNull(capturedCommand);
            Assert.False(capturedCommand.Parameters.Contains("@Definition"));
        }

        private static SqlStoreClient CreateClient(ISqlRetryService sqlRetryService, int currentSchemaVersion)
        {
            var schemaInformation = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max)
            {
                Current = currentSchemaVersion,
            };

            return new SqlStoreClient(sqlRetryService, NullLogger<SqlStoreClient>.Instance, schemaInformation);
        }
    }
}
