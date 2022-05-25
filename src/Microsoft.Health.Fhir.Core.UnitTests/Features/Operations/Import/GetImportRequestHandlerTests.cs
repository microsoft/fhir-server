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
        private readonly IMediator _mediator;
        private IQueueClient _queueClient = Substitute.For<IQueueClient>();

        public GetImportRequestHandlerTests()
        {
            var collection = new ServiceCollection();
            collection.Add(x => new GetImportRequestHandler(_queueClient, DisabledFhirAuthorizationService.Instance)).Singleton().AsSelf().AsImplementedInterfaces();

            ServiceProvider provider = collection.BuildServiceProvider();
            _mediator = new Mediator(type => provider.GetService(type));
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenGettingAnExistingBulkImportTaskWithCompletedStatus_ThenHttpResponseCodeShouldBeOk()
        {
            ImportOrchestratorTaskResult orchestratorTaskResult = new ImportOrchestratorTaskResult()
            {
                TransactionTime = DateTime.Now,
                Request = "Request",
            };

            TaskInfo orchestratorTask = new TaskInfo()
            {
                Status = TaskStatus.Completed,
                Result = JsonConvert.SerializeObject(orchestratorTaskResult),
            };

            ImportProcessingTaskResult processingTaskResult = new ImportProcessingTaskResult()
            {
                ResourceLocation = "http://ResourceLocation",
                ResourceType = "Patient",
                SucceedCount = 1,
                FailedCount = 1,
                ErrorLogLocation = "http://ResourceLocation",
            };

            TaskInfo processingTask = new TaskInfo()
            {
                Status = TaskStatus.Completed,
                Result = JsonConvert.SerializeObject(processingTaskResult),
            };

            GetImportResponse result = await SetupAndExecuteGetBulkImportTaskByIdAsync(orchestratorTask, new List<TaskInfo>() { processingTask });

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.Equal(1, result.TaskResult.Output.Count);
            Assert.Equal(1, result.TaskResult.Error.Count);
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenGettingAnCompletedImportTaskWithFailure_ThenHttpResponseCodeShouldBeExpected()
        {
            ImportOrchestratorTaskErrorResult orchestratorTaskResult = new ImportOrchestratorTaskErrorResult()
            {
                HttpStatusCode = HttpStatusCode.BadRequest,
                ErrorMessage = "error",
            };

            TaskInfo orchestratorTask = new TaskInfo()
            {
                Status = TaskStatus.Failed,
                Result = JsonConvert.SerializeObject(orchestratorTaskResult),
            };

            OperationFailedException ofe = await Assert.ThrowsAsync<OperationFailedException>(() => SetupAndExecuteGetBulkImportTaskByIdAsync(orchestratorTask, new List<TaskInfo>()));

            Assert.Equal(HttpStatusCode.BadRequest, ofe.ResponseStatusCode);
            Assert.NotNull(ofe.Message);
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenGettingAnExistingBulkImportTaskThatWasCanceled_ThenOperationFailedExceptionIsThrownWithBadRequestHttpResponseCode()
        {
            TaskInfo orchestratorTask = new TaskInfo()
            {
                Status = TaskStatus.Cancelled,
            };
            OperationFailedException ofe = await Assert.ThrowsAsync<OperationFailedException>(() => SetupAndExecuteGetBulkImportTaskByIdAsync(orchestratorTask, new List<TaskInfo>()));

            Assert.Equal(HttpStatusCode.BadRequest, ofe.ResponseStatusCode);
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenGettingAnExistingBulkImportTaskWithNotCompletedStatus_ThenHttpResponseCodeShouldBeAccepted()
        {
            ImportOrchestratorTaskResult orchestratorTaskResult = new ImportOrchestratorTaskResult()
            {
                TransactionTime = DateTime.Now,
                Request = "Request",
            };

            TaskInfo orchestratorTask = new TaskInfo()
            {
                Status = TaskStatus.Running,
                Result = JsonConvert.SerializeObject(orchestratorTaskResult),
            };

            ImportProcessingTaskResult processingTaskResult = new ImportProcessingTaskResult()
            {
                ResourceLocation = "http://ResourceLocation",
                ResourceType = "Patient",
                SucceedCount = 1,
                FailedCount = 1,
                ErrorLogLocation = "http://ResourceLocation",
            };

            TaskInfo processingTask1 = new TaskInfo()
            {
                Status = TaskStatus.Completed,
                Result = JsonConvert.SerializeObject(processingTaskResult),
            };

            TaskInfo processingTask2 = new TaskInfo()
            {
                Status = TaskStatus.Completed,
                Result = JsonConvert.SerializeObject(processingTaskResult),
            };

            TaskInfo processingTask3 = new TaskInfo()
            {
                Status = TaskStatus.Running,
                Result = JsonConvert.SerializeObject(processingTaskResult),
            };

            GetImportResponse result = await SetupAndExecuteGetBulkImportTaskByIdAsync(orchestratorTask, new List<TaskInfo>() { processingTask1, processingTask2, processingTask3 });

            Assert.Equal(HttpStatusCode.Accepted, result.StatusCode);
            Assert.Equal(2, result.TaskResult.Output.Count);
            Assert.Equal(2, result.TaskResult.Error.Count);
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenGettingWithNotExistTask_ThenNotFoundShouldBeReturned()
        {
            await Assert.ThrowsAsync<ResourceNotFoundException>(async () => await _mediator.GetImportStatusAsync(1, CancellationToken.None));
        }

        private async Task<GetImportResponse> SetupAndExecuteGetBulkImportTaskByIdAsync(TaskInfo orchestratorTaskInfo, List<TaskInfo> processingTaskInfos)
        {
            _queueClient.GetTaskByIdAsync(Arg.Any<byte>(), Arg.Any<long>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(orchestratorTaskInfo);

            List<TaskInfo> allTasks = new List<TaskInfo>(processingTaskInfos);
            allTasks.Add(orchestratorTaskInfo);
            _queueClient.GetTaskByGroupIdAsync(Arg.Any<byte>(), Arg.Any<long>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(allTasks);

            return await _mediator.GetImportStatusAsync(orchestratorTaskInfo.Id, CancellationToken.None);
        }
    }
}
