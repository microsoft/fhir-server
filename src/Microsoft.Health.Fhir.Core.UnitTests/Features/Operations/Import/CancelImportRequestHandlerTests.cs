// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Import;
using Microsoft.Health.TaskManagement;
using NSubstitute;
using Xunit;
using TaskStatus = Microsoft.Health.TaskManagement.TaskStatus;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.BulkImport
{
    public class CancelImportRequestHandlerTests
    {
        private const string TaskId = "taskId";

        private readonly ITaskManager _taskManager = Substitute.For<ITaskManager>();
        private readonly IMediator _mediator;

        private readonly CancellationToken _cancellationToken = new CancellationTokenSource().Token;

        private Func<int, TimeSpan> _sleepDurationProvider = new Func<int, TimeSpan>(retryCount => TimeSpan.FromSeconds(0));

        public CancelImportRequestHandlerTests()
        {
            var collection = new ServiceCollection();
            collection
                .Add(sp => new CancelImportRequestHandler(
                    _taskManager,
                    DisabledFhirAuthorizationService.Instance,
                    NullLogger<CancelImportRequestHandler>.Instance))
                .Singleton()
                .AsSelf()
                .AsImplementedInterfaces();

            ServiceProvider provider = collection.BuildServiceProvider();
            _mediator = new Mediator(type => provider.GetService(type));
        }

        [Theory]
        [InlineData(TaskStatus.Completed)]
        public async Task GivenAFhirMediator_WhenCancelingExistingBulkImportTaskThatHasAlreadyCompleted_ThenConflictStatusCodeShouldBeReturned(TaskStatus taskStatus)
        {
            OperationFailedException operationFailedException = await Assert.ThrowsAsync<OperationFailedException>(async () => await SetupAndExecuteCancelImportAsync(taskStatus, HttpStatusCode.Conflict));

            Assert.Equal(HttpStatusCode.Conflict, operationFailedException.ResponseStatusCode);
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenCancelingExistingBulkImportTaskThatHasAlreadyCanceled_ThenAcceptedCodeShouldBeReturned()
        {
            await SetupAndExecuteCancelImportAsync(TaskStatus.Queued, HttpStatusCode.Accepted, true);
        }

        [Theory]
        [InlineData(TaskStatus.Queued)]
        [InlineData(TaskStatus.Running)]
        public async Task GivenAFhirMediator_WhenCancelingExistingBulkImportTaskThatHasNotCompleted_ThenAcceptedStatusCodeShouldBeReturned(TaskStatus taskStatus)
        {
            TaskInfo taskInfo = await SetupAndExecuteCancelImportAsync(taskStatus, HttpStatusCode.Accepted);

            await _taskManager.Received(1).CancelTaskAsync(taskInfo.TaskId, _cancellationToken);
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenCancelingWithNotExistTask_ThenNotFoundShouldBeReturned()
        {
            _taskManager.CancelTaskAsync(Arg.Any<string>(), _cancellationToken).Returns<Task<TaskInfo>>(_ => throw new TaskNotExistException("Task not exist."));
            await Assert.ThrowsAsync<ResourceNotFoundException>(async () => await _mediator.CancelImportAsync(TaskId, _cancellationToken));
        }

        private async Task<TaskInfo> SetupAndExecuteCancelImportAsync(TaskStatus taskStatus, HttpStatusCode expectedStatusCode, bool isCanceled = false)
        {
            TaskInfo taskInfo = SetupBulkImportTask(taskStatus, isCanceled);

            CancelImportResponse response = await _mediator.CancelImportAsync(TaskId, _cancellationToken);

            Assert.NotNull(response);
            Assert.Equal(expectedStatusCode, response.StatusCode);

            return taskInfo;
        }

        private TaskInfo SetupBulkImportTask(TaskStatus taskStatus, bool isCanceled)
        {
            var taskInfo = new TaskInfo
            {
                TaskId = TaskId,
                QueueId = "0",
                Status = taskStatus,
                TaskTypeId = ImportProcessingTask.ImportProcessingTaskId,
                InputData = string.Empty,
                IsCanceled = isCanceled,
            };

            _taskManager.GetTaskAsync(TaskId, _cancellationToken).Returns(taskInfo);
            _taskManager.CancelTaskAsync(TaskId, _cancellationToken).Returns(taskInfo);

            return taskInfo;
        }
    }
}
