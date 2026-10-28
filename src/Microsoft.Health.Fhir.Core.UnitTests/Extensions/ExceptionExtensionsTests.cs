// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Data.SqlClient;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.UnitTests.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Extensions
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.ExceptionHandling)]
    public class ExceptionExtensionsTests
    {
        [Theory]
        [InlineData(-2)]
        [InlineData(30)]
        [InlineData(1222)]
        public void GivenSqlExceptionWithTimeoutErrorCode_WhenCheckingExecutionTimeout_ThenReturnsTrue(int errorCode)
        {
            var exception = SqlExceptionFactory.GetSqlException(errorCode, "SQL error");

            Assert.True(exception.IsExecutionTimeout());
        }

        [Theory]
        [InlineData("timeout expired")]
        [InlineData("EXECUTION TIMEOUT")]
        public void GivenSqlExceptionWithTimeoutMessage_WhenCheckingExecutionTimeout_ThenReturnsTrue(string message)
        {
            var exception = SqlExceptionFactory.GetSqlException(message, innerException: null);

            Assert.True(exception.IsExecutionTimeout());
        }

        [Fact]
        public void GivenNonSqlExceptionContainingExecutionTimeoutExpired_WhenCheckingExecutionTimeout_ThenReturnsTrue()
        {
            var exception = new InvalidOperationException("Execution timeout expired.");

            Assert.True(exception.IsExecutionTimeout());
        }

        [Fact]
        public void GivenExceptionWithoutTimeout_WhenCheckingExecutionTimeout_ThenReturnsFalse()
        {
            var exception = new InvalidOperationException("Operation failed.");

            Assert.False(exception.IsExecutionTimeout());
        }

        [Fact]
        public void GivenRequestRateExceededException_WhenConvertingOrChecking_ThenReturnsIt()
        {
            var exception = new RequestRateExceededException(TimeSpan.FromSeconds(1));

            Assert.Same(exception, exception.AsRequestRateExceeded());
            Assert.True(exception.IsRequestRateExceeded());
        }

        [Fact]
        public void GivenWrappedRequestRateExceededException_WhenConvertingOrChecking_ThenReturnsInnerException()
        {
            var requestRateExceededException = new RequestRateExceededException(TimeSpan.FromSeconds(1));
            var exception = new Exception("Wrapper", requestRateExceededException);

            Assert.Same(requestRateExceededException, exception.AsRequestRateExceeded());
            Assert.True(exception.IsRequestRateExceeded());
        }

        [Fact]
        public void GivenOtherException_WhenCheckingForRequestRateExceeded_ThenReturnsFalse()
        {
            var exception = new InvalidOperationException("Other error");

            Assert.Null(exception.AsRequestRateExceeded());
            Assert.False(exception.IsRequestRateExceeded());
        }

        [Fact]
        public void GivenRequestEntityTooLargeException_WhenChecking_ThenReturnsTrue()
        {
            var exception = new RequestEntityTooLargeException("Too large");

            Assert.True(exception.IsRequestEntityTooLarge());
        }

        [Fact]
        public void GivenWrappedRequestEntityTooLargeException_WhenChecking_ThenReturnsTrue()
        {
            var exception = new Exception("Wrapper", new RequestEntityTooLargeException("Too large"));

            Assert.True(exception.IsRequestEntityTooLarge());
        }

        [Fact]
        public void GivenOtherException_WhenCheckingForRequestEntityTooLarge_ThenReturnsFalse()
        {
            var exception = new InvalidOperationException("Other error");

            Assert.False(exception.IsRequestEntityTooLarge());
        }

        [Fact]
        public void GivenCompletedSqlTransactionMessage_WhenChecking_ThenReturnsTrueIgnoringCase()
        {
            var exception = new InvalidOperationException("THIS SQLTRANSACTION HAS COMPLETED; IT IS NO LONGER USABLE.");

            Assert.True(exception.IsCompletedTransactionException());
        }

        [Fact]
        public void GivenOtherInvalidOperationMessage_WhenCheckingForCompletedTransaction_ThenReturnsFalse()
        {
            var exception = new InvalidOperationException("A different operation error.");

            Assert.False(exception.IsCompletedTransactionException());
        }

        [Fact]
        public void GivenCustomerManagedKeySqlErrorCode_WhenChecking_ThenReturnsTrue()
        {
            foreach (int errorCode in SqlServerErrorCodes.CustomerManagedKeyErrorCodes)
            {
                SqlException exception = SqlExceptionFactory.GetSqlException(errorCode, "SQL error");

                Assert.True(exception.IsCustomerManagedKeyException());
            }
        }

        [Fact]
        public void GivenCustomerManagedKeySqlErrorMessage_WhenChecking_ThenReturnsTrueIgnoringCase()
        {
            SqlException exception = SqlExceptionFactory.GetSqlException(
                "The database is not accessible due to AZURE KEY VAULT CRITICAL ERROR.",
                innerException: null);

            Assert.True(exception.IsCustomerManagedKeyException());
        }

        [Fact]
        public void GivenUnrelatedSqlOrNonSqlException_WhenCheckingForCustomerManagedKey_ThenReturnsFalse()
        {
            SqlException sqlException = SqlExceptionFactory.GetSqlException(9999, "Unrelated SQL error");
            var nonSqlException = new InvalidOperationException("The operation failed.");

            Assert.False(sqlException.IsCustomerManagedKeyException());
            Assert.False(nonSqlException.IsCustomerManagedKeyException());
        }
    }
}
