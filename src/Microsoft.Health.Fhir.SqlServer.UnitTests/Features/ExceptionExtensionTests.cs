// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Fhir.SqlServer.Features;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer.Features.Storage;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features
{
    /// <summary>
    /// Unit tests for ExceptionExtension.
    /// Tests error pattern detection for retry logic and timeout detection.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class ExceptionExtensionTests
    {
        [Theory]
        [InlineData("semaphore timeout occurred")]
        [InlineData("connection attempt failed")]
        [InlineData("transport-level error has occurred")]
        [InlineData("connection timeout expired")]
        [InlineData("existing connection was forcibly closed by the remote host")]
        [InlineData("Semaphore Timeout Occurred")] // Case insensitive
        [InlineData("connected host has failed to respond")]
        [InlineData("operation on a socket could not be performed")]
        [InlineData("connection is closed")]
        [InlineData("severe error occurred")]
        [InlineData("connection was recovered and rowcount in the first query is not available")]
        public void GivenExceptionWithNetworkErrorPattern_WhenIsRetriable_ThenReturnsTrue(string errorMessage)
        {
            var exception = new Exception(errorMessage);

            var result = exception.IsRetriable();

            Assert.True(result);
        }

        [Fact]
        public void GivenExceptionWithDeadlockPattern_WhenIsRetriable_ThenReturnsTrue()
        {
            var exception = new Exception("Transaction was deadlocked on lock resources");

            var result = exception.IsRetriable();

            Assert.True(result);
        }

        [Theory]
        [InlineData("app domain with specified version id was unloaded due to memory pressure")]
        [InlineData("service has encountered an error processing your request. please try again")]
        public void GivenExceptionWithInternalSqlErrorPattern_WhenIsRetriable_ThenReturnsTrue(string errorMessage)
        {
            var exception = new Exception(errorMessage);

            var result = exception.IsRetriable();

            Assert.True(result);
        }

        [Theory]
        [InlineData("unable to access database 'TestDB' because it lacks a quorum of nodes for high availability")]
        [InlineData("database 'TestDB' is not currently available")]
        [InlineData("transaction log for database 'TestDB' is full due to 'AVAILABILITY_REPLICA'")]
        [InlineData("Login failed for user 'testuser'")]
        [InlineData("error occurred while establishing a connection")]
        [InlineData("connection is broken and recovery is not possible")]
        [InlineData("availability replica was triggered and ghost records are being deleted")]
        [InlineData("the definition of object 'SomeProc' has changed since it was compiled")]
        [InlineData("object accessed by the statement has been modified by a ddl statement")]
        [InlineData("the database has reached its size quota")]
        [InlineData("connections to this database are no longer allowed")]
        [InlineData("database is in emergency mode")]
        [InlineData("transaction log for database 'TestDB' is full due to 'ACTIVE_BACKUP_OR_RESTORE'")]
        [InlineData("The timeout period elapsed prior to obtaining a connection from the pool")]
        public void GivenExceptionWithDatabaseAvailabilityPattern_WhenIsRetriable_ThenReturnsTrue(string errorMessage)
        {
            var exception = new Exception(errorMessage);

            var result = exception.IsRetriable();

            Assert.True(result);
        }

        [Fact]
        public void GivenExceptionWithDatabaseOverloadPattern_WhenIsRetriable_ThenReturnsTrue()
        {
            var exception = new Exception("The request limit for the database is 200 and has been reached");

            var result = exception.IsRetriable();

            Assert.True(result);
        }

        [Theory]
        [InlineData("Some random error message")]
        [InlineData("Invalid operation")]
        [InlineData("")]
        public void GivenExceptionWithoutRetriablePattern_WhenIsRetriable_ThenReturnsFalse(string errorMessage)
        {
            var exception = new Exception(errorMessage);

            var result = exception.IsRetriable();

            Assert.False(result);
        }

        [Fact]
        public void GivenNestedExceptionWithRetriablePattern_WhenIsRetriable_ThenReturnsTrue()
        {
            var innerException = new Exception("deadlock detected");
            var outerException = new Exception("Outer error", innerException);

            var result = outerException.IsRetriable();

            Assert.True(result);
        }

        [Theory]
        [InlineData("execution timeout expired")]
        [InlineData("Execution Timeout Expired")] // Case insensitive
        public void GivenExceptionWithTimeoutPattern_WhenIsExecutionTimeout_ThenReturnsTrue(string errorMessage)
        {
            var exception = new Exception(errorMessage);

            var result = exception.IsExecutionTimeout();

            Assert.True(result);
        }

        [Theory]
        [InlineData("Some random error")]
        [InlineData("timeout occurred")] // Not "execution timeout"
        public void GivenExceptionWithoutTimeoutPattern_WhenIsExecutionTimeout_ThenReturnsFalse(string errorMessage)
        {
            var exception = new Exception(errorMessage);

            var result = exception.IsExecutionTimeout();

            Assert.False(result);
        }

        [Fact]
        public void GivenNestedExceptionWithTimeoutPattern_WhenIsExecutionTimeout_ThenReturnsTrue()
        {
            var innerException = new Exception("execution timeout expired");
            var outerException = new Exception("Wrapper error", innerException);

            var result = outerException.IsExecutionTimeout();

            Assert.True(result);
        }

        [Fact]
        public void GivenComplexRealWorldErrorMessage_WhenIsRetriable_ThenDetectsPatternCorrectly()
        {
            var exception = new Exception(
                "A transport-level error has occurred when receiving results from the server. " +
                "(provider: TCP Provider, error: 0 - An existing connection was forcibly closed by the remote host.)");

            var result = exception.IsRetriable();

            Assert.True(result);
        }

        [Fact]
        public void GivenExceptionWithIncorrectAsyncCallPattern_WhenIsRetriable_ThenReturnsTrue()
        {
            var exception = new Exception("This method may not be called when another read operation is pending");

            var result = exception.IsRetriable();

            Assert.True(result);
        }

        [Theory]
        [InlineData("remote procedure call protocol stream is incorrect")]
        public void GivenExceptionWithRemoteProcedureCallError_WhenIsRetriable_ThenReturnsTrue(string errorMessage)
        {
            var exception = new Exception(errorMessage);

            var result = exception.IsRetriable();

            Assert.True(result);
        }

        [Theory]
        [InlineData(SqlErrorCodes.KeyVaultCriticalError)]
        [InlineData(SqlErrorCodes.KeyVaultEncounteredError)]
        [InlineData(SqlErrorCodes.KeyVaultErrorObtainingInfo)]
        public void GivenSqlExceptionWithCmkErrorNumber_WhenCheckingIsCustomerManagedKeyException_ThenReturnsTrue(int sqlCmkErrorCode)
        {
            SqlException exception = SqlExceptionUtils.CreateSqlException(sqlCmkErrorCode); // CMK error code
            Assert.True(exception.IsCustomerManagedKeyException());
        }

        [Fact]
        public void GivenSqlExceptionWithDifferentErrorNumber_WhenCheckingIsCustomerManagedKeyException_ThenReturnsFalse()
        {
            SqlException exception = SqlExceptionUtils.CreateSqlException(1205); // Deadlock error
            Assert.False(exception.IsCustomerManagedKeyException());
        }

        [Fact]
        public void GivenNullException_WhenCheckingIsCustomerManagedKeyException_ThenReturnsFalse()
        {
            Exception exception = null;
            Assert.False(exception.IsCustomerManagedKeyException());
        }

        [Fact]
        public void GivenNonSqlException_WhenCheckingIsCustomerManagedKeyException_ThenReturnsFalse()
        {
            var exception = new InvalidOperationException("Not a SQL exception");
            Assert.False(exception.IsCustomerManagedKeyException());
        }

        [Theory]
        [InlineData("(..) is not accessible due to Azure Key Vault critical error (...)")]
        [InlineData("(...) is not accessible due to Azure Key Vault encountered an error (...)")]
        [InlineData("(...) is not accessible due to Azure Key Vault error obtaining information (...)")]

        public void GivenSqlExceptionWithKeyVaultErrorMessage_WhenCheckingIsCustomerManagedKeyException_ThenReturnsTrue(string errorMessage)
        {
            var exception = SqlExceptionUtils.CreateSqlException("Azure Key Vault critical error", new InvalidOperationException(errorMessage));
            Assert.True(exception.IsCustomerManagedKeyException());
        }
    }
}
