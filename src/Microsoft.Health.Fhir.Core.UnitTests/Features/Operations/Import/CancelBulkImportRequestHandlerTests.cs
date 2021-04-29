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
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Import;
using Microsoft.Health.Fhir.TaskManagement;
using NSubstitute;
using Xunit;
using TaskStatus = Microsoft.Health.Fhir.TaskManagement.TaskStatus;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.BulkImport
{
    public class CancelBulkImportRequestHandlerTests
    {
        private const string TaskId = "taskId";

        private readonly ITaskManager _taskManager = Substitute.For<ITaskManager>();
        private readonly IMediator _mediator;

        private readonly CancellationToken _cancellationToken = new CancellationTokenSource().Token;

        private Func<int, TimeSpan> _sleepDurationProvider = new Func<int, TimeSpan>(retryCount => TimeSpan.FromSeconds(0));

        public CancelBulkImportRequestHandlerTests()
        {
            var collection = new ServiceCollection();
            collection
                .Add(sp => new CancelBulkImportRequestHandler(
                    _taskManager,
                    DisabledFhirAuthorizationService.Instance,
                    NullLogger<CancelBulkImportRequestHandler>.Instance))
                .Singleton()
                .AsSelf()
                .AsImplementedInterfaces();

            ServiceProvider provider = collection.BuildServiceProvider();
            _mediator = new Mediator(type => provider.GetService(type));
        }

        [Theory(Skip = "not implemented")]
        [InlineData(TaskStatus.Completed)]
        public async Task GivenAFhirMediator_WhenCancelingExistingBulkImportTaskThatHasAlreadyCompleted_ThenConflictStatusCodeShouldBeReturned(TaskStatus taskStatus)
        {
            TaskInfo taskInfo = await SetupAndExecuteCancelExportAsync(taskStatus, HttpStatusCode.Conflict);

            Assert.Equal(taskStatus, taskInfo.Status);
        }

        [Fact(Skip = "not implemented")]
        public async Task GivenAFhirMediator_WhenCancelingExistingBulkImportTaskThatHasAlreadyCanceled_ThenConflictStatusCodeShouldBeReturned()
        {
            await SetupAndExecuteCancelExportAsync(TaskStatus.Queued, HttpStatusCode.Conflict, true);
        }

        [Theory(Skip = "not implemented")]
        [InlineData(TaskStatus.Queued)]
        [InlineData(TaskStatus.Running)]
        public async Task GivenAFhirMediator_WhenCancelingExistingBulkImportTaskThatHasNotCompleted_ThenAcceptedStatusCodeShouldBeReturned(TaskStatus taskStatus)
        {
            TaskInfo taskInfo = await SetupAndExecuteCancelExportAsync(taskStatus, HttpStatusCode.Accepted);

            await _taskManager.Received(1).CancelTaskAsync(taskInfo.TaskId, _cancellationToken);
        }

        private async Task<TaskInfo> SetupAndExecuteCancelExportAsync(TaskStatus taskStatus, HttpStatusCode expectedStatusCode, bool isCanceled = false)
        {
            TaskInfo taskInfo = SetupBulkImportTask(taskStatus, isCanceled);

            CancelImportResponse response = await _mediator.CancelBulkImportAsync(TaskId, _cancellationToken);

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
                TaskTypeId = ImportTask.ResourceImportTaskId,
                InputData = string.Empty,
                IsCanceled = isCanceled,
            };

            _taskManager.GetTaskAsync(TaskId, _cancellationToken).Returns(taskInfo);

            return taskInfo;
        }
    }
}
