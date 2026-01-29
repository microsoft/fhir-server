// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.SqlServer.Features;
using Microsoft.Health.Fhir.Tests.Common;
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
    }
}
