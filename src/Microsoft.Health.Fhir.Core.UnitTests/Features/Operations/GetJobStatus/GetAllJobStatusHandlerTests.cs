// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.GetJobStatus;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.GetJobStatus
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class GetAllJobStatusHandlerTests
    {
        [Fact]
        public async Task GivenMoreJobsThanOnePage_WhenGettingStatus_ThenOnlyOnePageIsReturned()
        {
            // Arrange
            var service = Substitute.For<IJobStatusService>();
            service.GetAllJobStatusAsync(null, 11, null, null, CancellationToken.None).Returns(
                Enumerable.Range(1, 11)
                    .Select(id => new JobStatusInfo { GroupId = 12 - id, QueueType = QueueType.Export })
                    .ToArray());
            var handler = new GetAllJobStatusHandler(service, Options.Create(new OperationsConfiguration()));

            // Act
            var response = await handler.HandleAsync(new GetAllJobStatusRequest(), CancellationToken.None);

            // Assert
            Assert.Equal(10, response.Jobs.Count);
            Assert.Equal(Enumerable.Range(2, 10).Reverse().Select(id => (long)id), response.Jobs.Select(job => job.GroupId));
            Assert.Equal("1|0|2|1", Encoding.UTF8.GetString(Convert.FromBase64String(response.ContinuationToken)));
        }

        [Fact]
        public async Task GivenAContinuationToken_WhenGettingTheNextPage_ThenConfiguredSizeAndCursorAreApplied()
        {
            // Arrange
            var service = Substitute.For<IJobStatusService>();
            service.GetAllJobStatusAsync(null, 3, null, null, CancellationToken.None).Returns(
                new[]
                {
                    new JobStatusInfo { GroupId = 101, QueueType = QueueType.Export },
                    new JobStatusInfo { GroupId = 100, QueueType = QueueType.Import },
                    new JobStatusInfo { GroupId = 99, QueueType = QueueType.Reindex },
                });
            service.GetAllJobStatusAsync(null, 3, 100, QueueType.Import, CancellationToken.None).Returns(
                new[]
                {
                    new JobStatusInfo { GroupId = 99, QueueType = QueueType.Reindex },
                    new JobStatusInfo { GroupId = 98, QueueType = QueueType.BulkUpdate },
                });
            var handler = new GetAllJobStatusHandler(service, Options.Create(new OperationsConfiguration
            {
                JobStatus = new JobStatusConfiguration { PageSize = 2 },
            }));

            // Act
            var first = await handler.HandleAsync(new GetAllJobStatusRequest(), CancellationToken.None);
            var second = await handler.HandleAsync(new GetAllJobStatusRequest(continuationToken: first.ContinuationToken), CancellationToken.None);

            // Assert
            Assert.Equal(new long[] { 101, 100 }, first.Jobs.Select(job => job.GroupId));
            Assert.Equal(new long[] { 99, 98 }, second.Jobs.Select(job => job.GroupId));
            Assert.Null(second.ContinuationToken);
        }

        [Theory]
        [InlineData("export", QueueType.Export)]
        [InlineData("import", QueueType.Import)]
        [InlineData("reindex", QueueType.Reindex)]
        [InlineData("bulk-delete", QueueType.BulkDelete)]
        [InlineData("bulk-update", QueueType.BulkUpdate)]
        public async Task GivenAJobType_WhenGettingStatus_ThenTheSelectedQueueIsReturned(string jobType, QueueType queueType)
        {
            // Arrange
            var service = Substitute.For<IJobStatusService>();
            service.GetAllJobStatusAsync(queueType, 11, null, null, CancellationToken.None).Returns(
                new[] { new JobStatusInfo { GroupId = 42, QueueType = queueType, JobType = jobType } });
            var handler = new GetAllJobStatusHandler(service, Options.Create(new OperationsConfiguration()));

            // Act
            var response = await handler.HandleAsync(new GetAllJobStatusRequest(jobType), CancellationToken.None);

            // Assert
            Assert.Equal(jobType, Assert.Single(response.Jobs).JobType);
            Assert.Null(response.ContinuationToken);
        }

        [Fact]
        public async Task GivenNoJobs_WhenGettingStatus_ThenAnEmptyLastPageIsReturned()
        {
            // Arrange
            var service = Substitute.For<IJobStatusService>();
            service.GetAllJobStatusAsync(null, 11, null, null, CancellationToken.None).Returns(Array.Empty<JobStatusInfo>());
            var handler = new GetAllJobStatusHandler(service, Options.Create(new OperationsConfiguration()));

            // Act
            var response = await handler.HandleAsync(new GetAllJobStatusRequest(), CancellationToken.None);

            // Assert
            Assert.Empty(response.Jobs);
            Assert.Null(response.ContinuationToken);
        }

        [Theory]
        [InlineData("")]
        [InlineData("defrag")]
        [InlineData("Unknown")]
        [InlineData("1")]
        [InlineData("export,import")]
        public async Task GivenAnUnsupportedJobType_WhenGettingStatus_ThenTheRequestIsRejected(string jobType)
        {
            // Arrange
            var handler = new GetAllJobStatusHandler(Substitute.For<IJobStatusService>(), Options.Create(new OperationsConfiguration()));

            // Act
            var action = () => handler.HandleAsync(new GetAllJobStatusRequest(jobType), CancellationToken.None);

            // Assert
            await Assert.ThrowsAsync<BadRequestException>(action);
        }

        [Theory]
        [InlineData("", null)]
        [InlineData("1|0|42|3", null)]
        [InlineData("1|0|42|255", null)]
        [InlineData("2|0|42|1", null)]
        [InlineData("1|0|9223372036854775808|1", null)]
        [InlineData("1|0|42|1|extra", null)]
        [InlineData("1|0|42|1", "export")]
        [InlineData("1|1|42|2", "export")]
        [InlineData("1|1|42|1", "import")]
        public async Task GivenAnInvalidCursor_WhenGettingStatus_ThenTheRequestIsRejected(string cursor, string jobType)
        {
            // Arrange
            var handler = new GetAllJobStatusHandler(Substitute.For<IJobStatusService>(), Options.Create(new OperationsConfiguration()));
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(cursor));

            // Act
            var action = () => handler.HandleAsync(new GetAllJobStatusRequest(jobType, token), CancellationToken.None);

            // Assert
            await Assert.ThrowsAsync<BadRequestException>(action);
        }

        [Theory]
        [InlineData("not base64")]
        [InlineData("................................................................................................................................................................................................")]
        public async Task GivenAMalformedToken_WhenGettingStatus_ThenTheRequestIsRejected(string token)
        {
            // Arrange
            var handler = new GetAllJobStatusHandler(Substitute.For<IJobStatusService>(), Options.Create(new OperationsConfiguration()));

            // Act
            var action = () => handler.HandleAsync(new GetAllJobStatusRequest(continuationToken: token), CancellationToken.None);

            // Assert
            await Assert.ThrowsAsync<BadRequestException>(action);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(int.MaxValue)]
        public void GivenAnInvalidPageSize_WhenConfiguringTheHandler_ThenConfigurationFails(int pageSize)
        {
            // Arrange
            var configuration = Options.Create(new OperationsConfiguration { JobStatus = new JobStatusConfiguration { PageSize = pageSize } });

            // Act
            var action = () => new GetAllJobStatusHandler(Substitute.For<IJobStatusService>(), configuration);

            // Assert
            Assert.Throws<InvalidOperationException>(action);
        }

        [Fact]
        public async Task GivenAStorageFailure_WhenGettingStatus_ThenTheFailureIsNotHidden()
        {
            // Arrange
            var service = Substitute.For<IJobStatusService>();
            var failure = new InvalidOperationException("Storage unavailable.");
            service.GetAllJobStatusAsync(null, 11, null, null, CancellationToken.None)
                .Returns(Task.FromException<IReadOnlyList<JobStatusInfo>>(failure));
            var handler = new GetAllJobStatusHandler(service, Options.Create(new OperationsConfiguration()));

            // Act
            var action = () => handler.HandleAsync(new GetAllJobStatusRequest(), CancellationToken.None);

            // Assert
            Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(action));
        }
    }
}
