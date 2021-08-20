// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
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
        [InlineData(TaskStatus.Queued)]
        [InlineData(TaskStatus.Created)]
        public async Task GivenAFhirMediator_WhenGettingAnExistingBulkImportTaskWithNotQueuedOrCreatedStatus_ThenHttpResponseCodeShouldBeAccepted(TaskStatus taskStatus)
        {
            GetImportResponse result = await SetupAndExecuteGetBulkImportTaskByIdAsync(taskStatus);

            Assert.Equal(HttpStatusCode.Accepted, result.StatusCode);
            Assert.NotNull(result.TaskResult);
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenGettingAnExistingBulkImportTaskWithRunningStatus_ThenHttpResponseCodeShouldBeAccepted()
        {
            (string context, string expectedResult) = SetupContext();
            GetImportResponse result = await SetupAndExecuteGetBulkImportTaskByIdAsync(TaskStatus.Running, false, null, context);
            ImportTaskResult actualResult = result.TaskResult;

            Assert.Equal(HttpStatusCode.Accepted, result.StatusCode);
            Assert.NotNull(actualResult);
            Assert.Equal(expectedResult, JsonConvert.SerializeObject(actualResult));
        }

        private async Task<GetImportResponse> SetupAndExecuteGetBulkImportTaskByIdAsync(TaskStatus taskStatus, bool isCanceled = false, TaskResultData resultData = null, string context = null)
        {
            // Result may be changed to real style result later
            ImportOrchestratorTaskInputData inputData = new ImportOrchestratorTaskInputData();
            var taskInfo = new TaskInfo
            {
                TaskId = TaskId,
                QueueId = "0",
                Status = taskStatus,
                TaskTypeId = ImportOrchestratorTask.ImportOrchestratorTaskId,
                InputData = JsonConvert.SerializeObject(inputData),
                IsCanceled = isCanceled,
                Context = context,
                Result = resultData != null ? JsonConvert.SerializeObject(resultData) : string.Empty,
            };

            _taskManager.GetTaskAsync(taskInfo.TaskId, Arg.Any<CancellationToken>()).Returns(taskInfo);
            return await _mediator.GetImportStatusAsync(taskInfo.TaskId, CancellationToken.None);
        }

        private (string context, string expectedResult) SetupContext()
        {
            List<ImportOperationOutcome> output = new List<ImportOperationOutcome>();
            List<ImportFailedOperationOutcome> error = new List<ImportFailedOperationOutcome>();
            ImportOrchestratorTaskContext orchestratorTaskContext = new ImportOrchestratorTaskContext();

            Uri completedTaskUri = new Uri("https://completed.ndjson/");
            ImportProcessingTaskResult processingTaskResult = new ImportProcessingTaskResult
            {
                ResourceType = "Patient",
                SucceedCount = 5000,
                FailedCount = 50,
                ErrorLogLocation = "https://PatientCompleted.ndjson/",
            };
            output.Add(new ImportOperationOutcome
            {
                Type = "Patient",
                Count = 5000,
                InputUrl = completedTaskUri,
            });
            error.Add(new ImportFailedOperationOutcome
            {
                Type = "Patient",
                Count = 50,
                InputUrl = completedTaskUri,
                Url = processingTaskResult.ErrorLogLocation,
            });
            TaskResultData resultData = new TaskResultData
            {
                Result = TaskResult.Success,
                ResultData = JsonConvert.SerializeObject(processingTaskResult),
            };
            TaskInfo completedProcessingTask = new TaskInfo
            {
                Status = TaskStatus.Completed,
                Result = JsonConvert.SerializeObject(resultData),
            };
            orchestratorTaskContext.DataProcessingTasks.Add(completedTaskUri, completedProcessingTask);

            Uri createdTaskUri = new Uri("https://created.ndjson/");
            TaskInfo createdProcessingTask = new TaskInfo
            {
                Status = TaskStatus.Created,
            };
            orchestratorTaskContext.DataProcessingTasks.Add(createdTaskUri, createdProcessingTask);

            Uri queuedTaskUri = new Uri("https://created.ndjson/");
            TaskInfo queuedProcessingTask = new TaskInfo
            {
                Status = TaskStatus.Queued,
            };
            orchestratorTaskContext.DataProcessingTasks.Add(queuedTaskUri, queuedProcessingTask);

            Uri runningProcessingTaskUri = new Uri("https://running.ndjson/");
            TaskInfo runningProcessingTask = new TaskInfo
            {
                Status = TaskStatus.Running,
            };
            orchestratorTaskContext.DataProcessingTasks.Add(runningProcessingTaskUri, runningProcessingTask);

            ImportTaskResult result = new ImportTaskResult
            {
                Output = output,
                Error = error,
            };
            string context = JsonConvert.SerializeObject(orchestratorTaskContext);
            string expectedResult = JsonConvert.SerializeObject(result);
            return (context, expectedResult);
        }
    }
}
