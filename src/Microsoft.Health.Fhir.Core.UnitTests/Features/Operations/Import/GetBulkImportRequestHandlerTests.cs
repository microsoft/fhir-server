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
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkImport;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Features.TaskManagement;
using Microsoft.Health.Fhir.Core.Messages.BulkImport;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.BulkImport
{
    public class GetBulkImportRequestHandlerTests
    {
        private const string TaskId = "taskId";
        private readonly ITaskManager _taskManager = Substitute.For<ITaskManager>();
        private readonly IMediator _mediator;
        private readonly Uri _createRequestUri = new Uri("https://localhost/$import/");
        private HttpStatusCode _failureStatusCode = HttpStatusCode.BadRequest;

        public GetBulkImportRequestHandlerTests()
        {
            var collection = new ServiceCollection();
            collection.Add(x => new GetBulkImportRequestHandler(_taskManager, DisabledFhirAuthorizationService.Instance)).Singleton().AsSelf().AsImplementedInterfaces();

            ServiceProvider provider = collection.BuildServiceProvider();
            _mediator = new Mediator(type => provider.GetService(type));
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenGettingAnExistingBulkImportTaskWithCompletedStatus_ThenHttpResponseCodeShouldBeOk()
        {
            GetBulkImportResponse result = await SetupAndExecuteGetBulkImportTaskByIdAsync(Core.Features.TaskManagement.TaskStatus.Completed);

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.NotNull(result.TaskResult);
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenGettingAnExistingBulkImportTaskThatWasCanceled_ThenOperationFailedExceptionIsThrownWithBadRequestHttpResponseCode()
        {
            OperationFailedException ofe = await Assert.ThrowsAsync<OperationFailedException>(() => SetupAndExecuteGetBulkImportTaskByIdAsync(Core.Features.TaskManagement.TaskStatus.Queued, true));

            Assert.NotNull(ofe);
            Assert.Equal(_failureStatusCode, ofe.ResponseStatusCode);
        }

        [Theory]
        [InlineData(Core.Features.TaskManagement.TaskStatus.Running)]
        [InlineData(Core.Features.TaskManagement.TaskStatus.Queued)]
        public async Task GivenAFhirMediator_WhenGettingAnExistingBulkImportTaskWithNotCompletedStatus_ThenHttpResponseCodeShouldBeAccepted(Core.Features.TaskManagement.TaskStatus taskStatus)
        {
            GetBulkImportResponse result = await SetupAndExecuteGetBulkImportTaskByIdAsync(taskStatus);

            Assert.Equal(HttpStatusCode.Accepted, result.StatusCode);
            Assert.Null(result.TaskResult);
        }

        private async Task<GetBulkImportResponse> SetupAndExecuteGetBulkImportTaskByIdAsync(Core.Features.TaskManagement.TaskStatus taskStatus, bool isCanceled = false)
        {
            // Result may be changed to real style result later
            var taskInfo = new TaskInfo
            {
                TaskId = TaskId,
                QueueId = "0",
                Status = taskStatus,
                TaskTypeId = (short)TaskType.BulkImport,
                InputData = string.Empty,
                IsCanceled = isCanceled,
                Result = taskStatus.ToString(),
            };

            _taskManager.GetTaskAsync(taskInfo.TaskId, Arg.Any<CancellationToken>()).Returns(taskInfo);

            return await _mediator.GetBulkImportStatusAsync(_createRequestUri, taskInfo.TaskId, CancellationToken.None);
        }
    }
}
