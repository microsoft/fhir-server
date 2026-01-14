// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Medino;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Import;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using JobStatus = Microsoft.Health.JobManagement.JobStatus;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.BulkImport
{
    [Trait(Traits.OwningTeam, OwningTeam.FhirImport)]
    [Trait(Traits.Category, Categories.Import)]
    public class CancelImportRequestHandlerTests
    {
        private const long JobId = 12345;

        private readonly IQueueClient _queueClient = Substitute.For<IQueueClient>();
        private readonly IMediator _mediator;

        private readonly CancellationToken _cancellationToken = new CancellationTokenSource().Token;

        private Func<int, TimeSpan> _sleepDurationProvider = new Func<int, TimeSpan>(retryCount => TimeSpan.FromSeconds(0));

        public CancelImportRequestHandlerTests()
        {
            var collection = new ServiceCollection();
            collection
                .Add(sp => new CancelImportRequestHandler(
                    _queueClient,
                    DisabledFhirAuthorizationService.Instance,
                    NullLogger<CancelImportRequestHandler>.Instance))
                .Singleton()
                .AsSelf()
                .AsImplementedInterfaces();

            ServiceProvider provider = collection.BuildServiceProvider();
            _mediator = new Mediator(provider);
        }

        [Theory]
        [InlineData(JobStatus.Completed)]
        [InlineData(JobStatus.Failed)]
        public async Task GivenAFhirMediator_WhenCancelingExistingBulkImportJobThatHasAlreadyCompleted_ThenConflictStatusCodeShouldBeReturned(JobStatus taskStatus)
        {
            OperationFailedException operationFailedException = await Assert.ThrowsAsync<OperationFailedException>(async () => await SetupAndExecuteCancelImportAsync(taskStatus, HttpStatusCode.Conflict));

            Assert.Equal(HttpStatusCode.Conflict, operationFailedException.ResponseStatusCode);
        }

        [Theory]
        [InlineData(JobStatus.Cancelled)]
        public async Task GivenAFhirMediator_WhenCancelingExistingBulkImportJobThatHasAlreadyBeenCanceled_ThenAcceptedStatusCodeShouldBeReturned(JobStatus taskStatus)
        {
            SetupBulkImportJob(taskStatus, true);
            CancelImportResponse response = await _mediator.CancelImportAsync(JobId, _cancellationToken);

            Assert.NotNull(response);
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        [Theory]
        [InlineData(JobStatus.Created)]
        [InlineData(JobStatus.Running)]
        public async Task GivenAFhirMediator_WhenCancelingExistingBulkImportJobThatHasNotCompleted_ThenAcceptedStatusCodeShouldBeReturned(JobStatus jobStatus)
        {
            List<JobInfo> jobs = await SetupAndExecuteCancelImportAsync(jobStatus, HttpStatusCode.Accepted);

            var groupJobId = jobs[0].GroupId;
            await _queueClient.Received(1).CancelJobByGroupIdAsync((byte)Core.Features.Operations.QueueType.Import, groupJobId, _cancellationToken);
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenCancelingWithNotExistJob_ThenNotFoundShouldBeReturned()
        {
            _queueClient.CancelJobByGroupIdAsync(Arg.Any<byte>(), Arg.Any<long>(), _cancellationToken).Returns<Task>(_ => throw new JobNotExistException("Task not exist."));
            await Assert.ThrowsAsync<ResourceNotFoundException>(async () => await _mediator.CancelImportAsync(JobId, _cancellationToken));
        }

        private async Task<List<JobInfo>> SetupAndExecuteCancelImportAsync(JobStatus jobStatus, HttpStatusCode expectedStatusCode, bool isCanceled = false)
        {
            List<JobInfo> jobs = SetupBulkImportJob(jobStatus, isCanceled);

            CancelImportResponse response = await _mediator.CancelImportAsync(JobId, _cancellationToken);

            Assert.NotNull(response);
            Assert.Equal(expectedStatusCode, response.StatusCode);

            return jobs;
        }

        private List<JobInfo> SetupBulkImportJob(JobStatus jobStatus, bool isCanceled)
        {
            var jobs = new List<JobInfo>()
            {
                new JobInfo
                {
                    Id = JobId,
                    GroupId = JobId,
                    Status = jobStatus,
                    Definition = string.Empty,
                    CancelRequested = isCanceled,
                },
            };

            _queueClient.GetJobByGroupIdAsync(Arg.Any<byte>(), JobId, Arg.Any<bool>(), _cancellationToken).Returns(jobs);

            return jobs;
        }
    }
}
