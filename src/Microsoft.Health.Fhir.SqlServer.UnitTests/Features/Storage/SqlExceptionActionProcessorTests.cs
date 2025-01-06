// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.SqlServer.Features.Storage;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Storage
{
    public class SqlExceptionActionProcessorTests
    {
        private readonly ILogger<SqlExceptionActionProcessor<object, Exception>> _logger;
        private readonly SqlExceptionActionProcessor<object, Exception> _processor;

        public SqlExceptionActionProcessorTests()
        {
            _logger = Substitute.For<ILogger<SqlExceptionActionProcessor<object, Exception>>>();
            _processor = new SqlExceptionActionProcessor<object, Exception>(_logger);
        }

        [Fact]
        public async Task GivenSqlException_WhenErrorCodeIsTimeoutExpired_ShouldThrowRequestTimeoutException()
        {
            // Arrange
            var sqlException = SqlExceptionFactory.Create(SqlErrorCodes.TimeoutExpired);
            var request = new object();

            // Act & Assert
            var exception = await Assert.ThrowsAsync<RequestTimeoutException>(() =>
                _processor.Execute(request, sqlException, CancellationToken.None));

            Assert.Equal(Resources.ExecutionTimeoutExpired, exception.Message);
        }

        private static class SqlExceptionFactory
        {
            public static SqlException Create(int errorNumber)
            {
                var errorCollection = CreateErrorCollection(errorNumber);
                var sqlException = (SqlException)Activator.CreateInstance(
                    typeof(SqlException),
                    BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    ["Test SQL Exception", errorCollection, null, Guid.NewGuid()],
                    null);

                return sqlException;
            }

            private static SqlErrorCollection CreateErrorCollection(int errorNumber)
            {
                var error = CreateSqlError(errorNumber);
                var errorCollection = (SqlErrorCollection)Activator.CreateInstance(
                    typeof(SqlErrorCollection),
                    BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    null,
                    null);

                typeof(SqlErrorCollection)
                    .GetMethod("Add", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.Invoke(errorCollection, [error]);

                return errorCollection;
            }

            private static SqlError CreateSqlError(int errorNumber)
            {
                return (SqlError)Activator.CreateInstance(
                    typeof(SqlError),
                    BindingFlags.NonPublic | BindingFlags.Instance,
                    null,
                    [errorNumber, (byte)0, (byte)0, "TestServer", "TestMessage", "TestProcedure", 0],
                    null);
            }
        }
    }
}
