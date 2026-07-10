// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data.SqlTypes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer.Features.Storage;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Storage
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class SqlExceptionActionProcessorTests
    {
        private readonly ILogger<SqlExceptionActionProcessor<string, SqlException>> _mockLogger;

        public SqlExceptionActionProcessorTests()
        {
            _mockLogger = Substitute.For<ILogger<SqlExceptionActionProcessor<string, SqlException>>>();
        }

        [Fact]
        public async Task GivenSqlTruncateException_WhenExecuting_ThenResourceSqlTruncateExceptionIsThrown()
        {
            // Arrange
            var sqlTruncateException = new SqlTruncateException("Truncate error");
            var logger = Substitute.For<ILogger<SqlExceptionActionProcessor<string, SqlTruncateException>>>();
            var processor = new SqlExceptionActionProcessor<string, SqlTruncateException>(logger);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ResourceSqlTruncateException>(() =>
                processor.Execute("test-request", sqlTruncateException, CancellationToken.None));

            Assert.Equal("Truncate error", exception.Message);

            // Verify logger
            logger.Received(1).LogError(
                sqlTruncateException,
                $"A {nameof(ResourceSqlTruncateException)} occurred while executing request");
        }

        [Theory]
        [InlineData(SqlErrorCodes.TimeoutExpired, typeof(RequestTimeoutException))]
        [InlineData(SqlErrorCodes.MethodNotAllowed, typeof(MethodNotAllowedException))]
        [InlineData(SqlErrorCodes.QueryProcessorNoQueryPlan, typeof(SqlQueryPlanException))]
        [InlineData(18456, typeof(LoginFailedForUserException))] // Login failed for user
        [InlineData(SqlErrorCodes.TooManyParameters, typeof(RequestNotValidException))]
        public async Task GivenSqlException_WhenExecuting_ThenSpecificExceptionIsThrown(int errorCode, Type expectedExceptionType)
        {
            // Arrange
            var sqlException = SqlExceptionUtils.CreateSqlException(errorCode);
            var processor = new SqlExceptionActionProcessor<string, SqlException>(_mockLogger);

            // Act & Assert
            await Assert.ThrowsAsync(expectedExceptionType, () =>
                processor.Execute("test-request", sqlException, CancellationToken.None));

            // Verify logger
            _mockLogger.Received(1).LogError(
                sqlException,
                $"A {nameof(SqlException)} occurred while executing request");
        }

        [Theory]
        [InlineData(SqlErrorCodes.KeyVaultCriticalError)]
        [InlineData(SqlErrorCodes.KeyVaultEncounteredError)]
        [InlineData(SqlErrorCodes.KeyVaultErrorObtainingInfo)]
        [InlineData(SqlErrorCodes.CannotConnectToDBInCurrentState)]
        public async Task GivenSqlExceptionWithCmkError_WhenExecuting_ThenCustomerManagedKeyExceptionIsThrown(int errorCode)
        {
            // Arrange
            var sqlException = SqlExceptionUtils.CreateSqlException(errorCode);
            var processor = new SqlExceptionActionProcessor<string, SqlException>(_mockLogger);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<CustomerManagedKeyException>(() =>
                processor.Execute("test-request", sqlException, CancellationToken.None));

            Assert.Equal(Core.Resources.OperationFailedForCustomerManagedKey, exception.Message);

            // Verify logger
            _mockLogger.Received(1).LogError(
                sqlException,
                $"A {nameof(SqlException)} occurred while executing request");
        }

        [Fact]
        public async Task GivenSqlExceptionWithUnhandledError_WhenExecuting_ThenResourceSqlExceptionIsThrown()
        {
            // Arrange
            var sqlException = SqlExceptionUtils.CreateSqlException(99999);
            var processor = new SqlExceptionActionProcessor<string, SqlException>(_mockLogger);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ResourceSqlException>(() =>
                processor.Execute("test-request", sqlException, CancellationToken.None));

            Assert.Equal(Core.Resources.InternalServerError, exception.Message);

            // Verify logger
            _mockLogger.Received(1).LogError(
                sqlException,
                $"A {nameof(SqlException)} occurred while executing request");
        }
    }
}
