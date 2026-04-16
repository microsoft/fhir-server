// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Storage
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class SqlQueueClientTests
    {
        private readonly ISqlRetryService _sqlRetryService;
        private readonly ILogger<SqlQueueClient> _logger;
        private readonly SchemaInformation _schemaInformation;

        public SqlQueueClientTests()
        {
            _sqlRetryService = Substitute.For<ISqlRetryService>();
            _logger = Substitute.For<ILogger<SqlQueueClient>>();
            _schemaInformation = new SchemaInformation(1, 100);
        }

        [Theory]
        [InlineData("retryService")]
        [InlineData("logger")]
        [InlineData("schemaInfo")]
        public void Constructor_WithNullParameter_ShouldThrowArgumentNullException(string nullParameter)
        {
            // Act & Assert
            switch (nullParameter)
            {
                case "retryService":
                    Assert.Throws<ArgumentNullException>(() => new SqlQueueClient(null, _logger));
                    break;
                case "logger":
                    Assert.Throws<ArgumentNullException>(() => new SqlQueueClient(_sqlRetryService, null));
                    break;
                case "schemaInfo":
                    Assert.Throws<ArgumentNullException>(() => new SqlQueueClient(null, _sqlRetryService, _logger));
                    break;
            }
        }

        [Fact]
        public async Task CompleteJobAsync_WhenPreconditionFailed_ShouldThrowJobNotExistException()
        {
            // Arrange
            var client = new SqlQueueClient(_schemaInformation, _sqlRetryService, _logger);
            var jobInfo = CreateJobInfo(1);
            jobInfo.Status = JobStatus.Failed;

            _sqlRetryService
                .ExecuteSql(
                    Arg.Any<SqlCommand>(),
                    Arg.Any<Func<SqlCommand, CancellationToken, Task>>(),
                    Arg.Any<ILogger>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    throw new JobNotExistException("Job does not exist");
                });

            // Act & Assert
            await Assert.ThrowsAsync<JobNotExistException>(
                () => client.CompleteJobAsync(jobInfo, false, CancellationToken.None));
        }

        [Fact]
        public async Task CompleteJobAsync_WithFailedStatus_ShouldSetFailedParameterTrue()
        {
            // Arrange
            var client = new SqlQueueClient(_schemaInformation, _sqlRetryService, _logger);
            var jobInfo = CreateJobInfo(1);
            jobInfo.Status = JobStatus.Failed;

            SqlCommand capturedCommand = null;
            _sqlRetryService
                .ExecuteSql(
                    Arg.Do<SqlCommand>(cmd => capturedCommand = cmd),
                    Arg.Any<Func<SqlCommand, CancellationToken, Task>>(),
                    Arg.Any<ILogger>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

            // Act
            await client.CompleteJobAsync(jobInfo, false, CancellationToken.None);

            // Assert
            Assert.NotNull(capturedCommand);
            var failedParam = capturedCommand.Parameters.Cast<SqlParameter>()
                .FirstOrDefault(p => p.ParameterName == "@Failed");
            Assert.NotNull(failedParam);
            Assert.True((bool)failedParam.Value);
        }

        [Fact]
        public async Task CompleteJobAsync_WithNullData_ShouldUseDBNull()
        {
            // Arrange
            var client = new SqlQueueClient(_schemaInformation, _sqlRetryService, _logger);
            var jobInfo = CreateJobInfo(1);
            jobInfo.Data = null;
            jobInfo.Status = JobStatus.Completed;

            SqlCommand capturedCommand = null;
            _sqlRetryService
                .ExecuteSql(
                    Arg.Do<SqlCommand>(cmd => capturedCommand = cmd),
                    Arg.Any<Func<SqlCommand, CancellationToken, Task>>(),
                    Arg.Any<ILogger>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);

            // Act
            await client.CompleteJobAsync(jobInfo, false, CancellationToken.None);

            // Assert
            Assert.NotNull(capturedCommand);
            var dataParam = capturedCommand.Parameters.Cast<SqlParameter>()
                .FirstOrDefault(p => p.ParameterName == "@Data");
            Assert.NotNull(dataParam);
            Assert.Equal(DBNull.Value, dataParam.Value);
        }

        [Fact]
        public void IsInitialized_WhenSchemaInfoNull_ShouldThrowInvalidOperationException()
        {
            // Arrange
            var client = new SqlQueueClient(_sqlRetryService, _logger);

            // Act & Assert
            var exception = Assert.Throws<InvalidOperationException>(() => client.IsInitialized());
            Assert.Contains("schema information = null", exception.Message);
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData(0, false)]
        [InlineData(100, true)]
        public void IsInitialized_WithVariousSchemaVersions_ShouldReturnExpectedResult(int? currentVersion, bool expectedResult)
        {
            // Arrange
            var schemaInfo = new SchemaInformation(1, 100);
            typeof(SchemaInformation).GetProperty(nameof(SchemaInformation.Current))
                .SetValue(schemaInfo, currentVersion);
            var client = new SqlQueueClient(schemaInfo, _sqlRetryService, _logger);

            // Act
            var result = client.IsInitialized();

            // Assert
            Assert.Equal(expectedResult, result);
        }

        private static JobInfo CreateJobInfo(long id)
        {
            return new JobInfo
            {
                Id = id,
                QueueType = 1,
                Status = JobStatus.Running,
                GroupId = 1,
                Definition = "test-definition",
                Version = 1,
                Result = null,
                Data = 100,
            };
        }
    }
}
