// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data.Common;
using System.Data.SqlTypes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.SqlServer.Features.Storage;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Storage
{
    public class SqlExceptionActionProcessorTests
    {
        private readonly ILogger<SqlExceptionActionProcessor<string, DbException>> _mockLogger;

        public SqlExceptionActionProcessorTests()
        {
            _mockLogger = Substitute.For<ILogger<SqlExceptionActionProcessor<string, DbException>>>();
        }

        [Fact]
        public async Task GivenSqlTruncateException_WhenExecuting_ThenResourceSqlTruncateExceptionIsThrown()
        {
            // Arrange
            var sqlTruncateException = new SqlTruncateException("Truncate error");
            var truncateLogger = Substitute.For<ILogger<SqlExceptionActionProcessor<string, SqlTruncateException>>>();
            var processor = new SqlExceptionActionProcessor<string, SqlTruncateException>(truncateLogger);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ResourceSqlTruncateException>(() =>
                processor.Execute("test-request", sqlTruncateException, CancellationToken.None));

            Assert.Equal("Truncate error", exception.Message);

            // Verify logger
            truncateLogger.Received(1).LogError(
                sqlTruncateException,
                $"A {nameof(ResourceSqlTruncateException)} occurred while executing request");
        }

        [Theory]
        [InlineData(SqlErrorCodes.TimeoutExpired, typeof(RequestTimeoutException))]
        [InlineData(SqlErrorCodes.MethodNotAllowed, typeof(MethodNotAllowedException))]
        [InlineData(SqlErrorCodes.QueryProcessorNoQueryPlan, typeof(SqlQueryPlanException))]
        [InlineData(18456, typeof(LoginFailedForUserException))] // 18456 is login failed for user
        public async Task GivenSqlException_WhenExecuting_ThenSpecificExceptionIsThrown(int errorCode, Type expectedExceptionType)
        {
            // Arrange
            var dbException = new MockDbException(errorCode);
            var processor = new SqlExceptionActionProcessor<string, DbException>(_mockLogger);

            // Act & Assert
            var exception = await Assert.ThrowsAsync(expectedExceptionType, () =>
                processor.Execute("test-request", dbException, CancellationToken.None));

            // Verify logger
            _mockLogger.Received(1).LogError(
                dbException,
                $"A {nameof(DbException)} occurred while executing request");
        }

        [Theory]
        [InlineData(SqlErrorCodes.KeyVaultCriticalError)]
        [InlineData(SqlErrorCodes.KeyVaultEncounteredError)]
        [InlineData(SqlErrorCodes.KeyVaultErrorObtainingInfo)]
        [InlineData(SqlErrorCodes.CannotConnectToDBInCurrentState)]
        public async Task GivenSqlExceptionWithCmkError_WhenExecuting_ThenCustomerManagedKeyExceptionIsThrown(int errorCode)
        {
            // Arrange
            var dbException = new MockDbException(errorCode);
            var processor = new SqlExceptionActionProcessor<string, DbException>(_mockLogger);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<CustomerManagedKeyException>(() =>
                processor.Execute("test-request", dbException, CancellationToken.None));

            Assert.Equal(Core.Resources.OperationFailedForCustomerManagedKey, exception.Message);

            // Verify logger
            _mockLogger.Received(1).LogError(
                dbException,
                $"A {nameof(DbException)} occurred while executing request");
        }

        [Fact]
        public async Task GivenDbExceptionWithUnhandledError_WhenExecuting_ThenResourceSqlExceptionIsThrown()
        {
            // Arrange
            var dbException = new MockDbException(9999);
            var processor = new SqlExceptionActionProcessor<string, DbException>(_mockLogger);

            // Act & Assert
            var exception = await Assert.ThrowsAsync<ResourceSqlException>(() =>
                processor.Execute("test-request", dbException, CancellationToken.None));

            Assert.Equal(Core.Resources.InternalServerError, exception.Message);

            // Verify logger
            _mockLogger.Received(1).LogError(
                dbException,
                $"A {nameof(DbException)} occurred while executing request");
        }

        private class MockDbException : DbException
        {
            public MockDbException(int errorCode, string message = "Simulated database error")
                : base(message)
            {
                ErrorCode = errorCode;
            }

            public override int ErrorCode { get; }
        }
    }
}
