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
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Import;
using Microsoft.Health.TaskManagement;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;
using TaskStatus = Microsoft.Health.TaskManagement.TaskStatus;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.BulkImport
{
    public class GetImportRequestHandlerTests
    {
        private const string TaskId = "taskId";
        private readonly ITaskManager _taskManager = Substitute.For<ITaskManager>();
        private readonly IMediator _mediator;
        private readonly Uri _createRequestUri = new Uri("https://localhost/$import/");
        private HttpStatusCode _failureStatusCode = HttpStatusCode.BadRequest;

        public GetImportRequestHandlerTests()
        {
            var collection = new ServiceCollection();
            collection.Add(x => new GetImportRequestHandler(_taskManager, DisabledFhirAuthorizationService.Instance)).Singleton().AsSelf().AsImplementedInterfaces();

            ServiceProvider provider = collection.BuildServiceProvider();
            _mediator = new Mediator(type => provider.GetService(type));
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenGettingAnExistingBulkImportTaskWithCompletedStatus_ThenHttpResponseCodeShouldBeOk()
        {
            ImportTaskResult expectedResult = new ImportTaskResult();
            expectedResult.Request = "test";

            TaskResultData taskResultData = new TaskResultData()
            {
                Result = TaskResult.Success,
                ResultData = JsonConvert.SerializeObject(expectedResult),
            };

            GetImportResponse result = await SetupAndExecuteGetBulkImportTaskByIdAsync(TaskStatus.Completed, false, taskResultData);

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.NotNull(result.TaskResult);
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenGettingAnCompletedImportTaskWithFailure_ThenHttpResponseCodeShouldBeExpected()
        {
            ImportTaskErrorResult expectedResult = new ImportTaskErrorResult();
            expectedResult.HttpStatusCode = HttpStatusCode.BadRequest;

            TaskResultData taskResultData = new TaskResultData()
            {
                Result = TaskResult.Fail,
                ResultData = JsonConvert.SerializeObject(expectedResult),
            };

            OperationFailedException ofe = await Assert.ThrowsAsync<OperationFailedException>(() => SetupAndExecuteGetBulkImportTaskByIdAsync(TaskStatus.Completed, false, taskResultData));

            Assert.Equal(HttpStatusCode.BadRequest, ofe.ResponseStatusCode);
            Assert.NotNull(ofe.Message);
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenGettingAnExistingBulkImportTaskThatWasCanceled_ThenOperationFailedExceptionIsThrownWithBadRequestHttpResponseCode()
        {
            OperationFailedException ofe = await Assert.ThrowsAsync<OperationFailedException>(() => SetupAndExecuteGetBulkImportTaskByIdAsync(TaskStatus.Queued, true));

            Assert.NotNull(ofe);
            Assert.Equal(_failureStatusCode, ofe.ResponseStatusCode);
        }

        [Theory]
        [InlineData(TaskStatus.Running)]
        [InlineData(TaskStatus.Queued)]
        public async Task GivenAFhirMediator_WhenGettingAnExistingBulkImportTaskWithNotCompletedStatus_ThenHttpResponseCodeShouldBeAccepted(TaskStatus taskStatus)
        {
            GetImportResponse result = await SetupAndExecuteGetBulkImportTaskByIdAsync(taskStatus);

            Assert.Equal(HttpStatusCode.Accepted, result.StatusCode);
            Assert.Null(result.TaskResult);
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenGettingWithNotExistTask_ThenNotFoundShouldBeReturned()
        {
            _taskManager.GetTaskAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<TaskInfo>(null));
            await Assert.ThrowsAsync<ResourceNotFoundException>(async () => await _mediator.GetImportStatusAsync(TaskId, CancellationToken.None));
        }

        private async Task<GetImportResponse> SetupAndExecuteGetBulkImportTaskByIdAsync(TaskStatus taskStatus, bool isCanceled = false, TaskResultData resultData = null)
        {
            // Result may be changed to real style result later
            var taskInfo = new TaskInfo
            {
                TaskId = TaskId,
                QueueId = "0",
                Status = taskStatus,
                TaskTypeId = ImportProcessingTask.ImportProcessingTaskId,
                InputData = string.Empty,
                IsCanceled = isCanceled,
                Result = resultData != null ? JsonConvert.SerializeObject(resultData) : string.Empty,
            };

            _taskManager.GetTaskAsync(taskInfo.TaskId, Arg.Any<CancellationToken>()).Returns(taskInfo);

            return await _mediator.GetImportStatusAsync(taskInfo.TaskId, CancellationToken.None);
        }
    }
}
