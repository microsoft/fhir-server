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
using Newtonsoft.Json;
using NSubstitute;
using Xunit;
using JobStatus = Microsoft.Health.JobManagement.JobStatus;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.BulkImport
{
    [Trait(Traits.OwningTeam, OwningTeam.FhirImport)]
    [Trait(Traits.Category, Categories.Import)]
    public class GetImportRequestHandlerTests
    {
        private readonly IMediator _mediator;
        private IQueueClient _queueClient = Substitute.For<IQueueClient>();

        public GetImportRequestHandlerTests()
        {
            var collection = new ServiceCollection();
            collection.Add(x => new GetImportRequestHandler(_queueClient, DisabledFhirAuthorizationService.Instance)).Singleton().AsSelf().AsImplementedInterfaces();

            ServiceProvider provider = collection.BuildServiceProvider();
            _mediator = new Mediator(provider);
        }

        [Fact]
        public async Task WhenGettingCompletedJob_ThenResponseCodeShouldBeOk()
        {
            var coordResult = new ImportOrchestratorJobResult() { Request = "Request" };
            var coord = new JobInfo() { Status = JobStatus.Completed, Result = JsonConvert.SerializeObject(coordResult), Definition = JsonConvert.SerializeObject(new ImportOrchestratorJobDefinition()) };
            var workerResult = new ImportProcessingJobResult() { SucceededResources = 1, FailedResources = 1, ErrorLogLocation = "http://xyz" };
            var worker = new JobInfo() { Id = 1, Status = JobStatus.Completed, Result = JsonConvert.SerializeObject(workerResult), Definition = JsonConvert.SerializeObject(new ImportProcessingJobDefinition() { ResourceLocation = "http://xyz" }) };

            var result = await SetupAndExecuteGetBulkImportJobByIdAsync(coord, [worker]);

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.Single(result.JobResult.Output);
            Assert.Single(result.JobResult.Error);
        }

        [Theory]
        [InlineData(HttpStatusCode.BadRequest)]
        [InlineData(HttpStatusCode.InternalServerError)]
        [InlineData((HttpStatusCode)0)]
        public async Task WhenGettingFailedJob_ThenExecptionIsTrownWithCorrectResponseCode(HttpStatusCode statusCode)
        {
            var coord = new JobInfo() { Status = JobStatus.Completed };
            var workerResult = new ImportJobErrorResult() { ErrorMessage = "Error", HttpStatusCode = statusCode };
            var worker = new JobInfo() { Id = 1, Status = JobStatus.Failed, Result = JsonConvert.SerializeObject(workerResult), Definition = JsonConvert.SerializeObject(new ImportProcessingJobDefinition() { ResourceLocation = "http://xyz" }) };
            var definition = JsonConvert.DeserializeObject<ImportProcessingJobDefinition>(worker.Definition);
            var resourceLocation = new Uri(definition.ResourceLocation);

            var ofe = await Assert.ThrowsAsync<OperationFailedException>(() => SetupAndExecuteGetBulkImportJobByIdAsync(coord, [worker]));

            Assert.Equal(statusCode == 0 ? HttpStatusCode.InternalServerError : statusCode, ofe.ResponseStatusCode);
            Assert.Equal(string.Format(Core.Resources.OperationFailedWithErrorFile, OperationsConstants.Import, ofe.ResponseStatusCode == HttpStatusCode.InternalServerError ? HttpStatusCode.InternalServerError : "Error", resourceLocation.OriginalString), ofe.Message);
        }

        [Fact]
        public async Task WhenGettingFailedJob_WithGenericException_ThenExecptionIsTrownWithCorrectResponseCode()
        {
            var coord = new JobInfo() { Status = JobStatus.Completed };
            object workerResult = new { message = "Error", stackTrace = "Trace" };
            var worker = new JobInfo() { Id = 1, Status = JobStatus.Failed, Result = JsonConvert.SerializeObject(workerResult), Definition = JsonConvert.SerializeObject(new ImportProcessingJobDefinition() { ResourceLocation = "http://xyz" }) };
            var definition = JsonConvert.DeserializeObject<ImportProcessingJobDefinition>(worker.Definition);
            var resourceLocation = new Uri(definition.ResourceLocation);

            var ofe = await Assert.ThrowsAsync<OperationFailedException>(() => SetupAndExecuteGetBulkImportJobByIdAsync(coord, [worker]));

            Assert.Equal(HttpStatusCode.InternalServerError, ofe.ResponseStatusCode);
            Assert.Equal(string.Format(Core.Resources.OperationFailedWithErrorFile, OperationsConstants.Import, HttpStatusCode.InternalServerError, resourceLocation.OriginalString), ofe.Message);
        }

        [Fact]
        public async Task WhenGettingImpprtWithCancelledOrchestratorJob_ThenExceptionIsThrownWithBadResponseCode()
        {
            var coord = new JobInfo() { Status = JobStatus.Cancelled };
            var ofe = await Assert.ThrowsAsync<OperationFailedException>(() => SetupAndExecuteGetBulkImportJobByIdAsync(coord, []));
            Assert.Equal(HttpStatusCode.BadRequest, ofe.ResponseStatusCode);
        }

        [Fact]
        public async Task WhenGettingImportWithCancelledWorkerJob_ThenExceptionIsThrownWithBadResponseCode()
        {
            var coord = new JobInfo() { Status = JobStatus.Completed };
            var worker = new JobInfo() { Id = 1, Status = JobStatus.Cancelled };
            var ofe = await Assert.ThrowsAsync<OperationFailedException>(() => SetupAndExecuteGetBulkImportJobByIdAsync(coord, [worker]));
            Assert.Equal(HttpStatusCode.BadRequest, ofe.ResponseStatusCode);
        }

        [Fact]
        public async Task WhenGettingInFlightJob_ThenResponseCodeShouldBeAccepted()
        {
            var coordResult = new ImportOrchestratorJobResult() { Request = "Request" };
            var coord = new JobInfo() { Status = JobStatus.Completed, Result = JsonConvert.SerializeObject(coordResult), Definition = JsonConvert.SerializeObject(new ImportOrchestratorJobDefinition()) };

            var workerResult = new ImportProcessingJobResult() { SucceededResources = 1, FailedResources = 1, ErrorLogLocation = "http://xyz" };

            // jobs 1 and 2 are created for the same input file, they are grouped together in the results
            var worker1 = new JobInfo()
            {
                Id = 1,
                Status = JobStatus.Completed,
                Result = JsonConvert.SerializeObject(workerResult),
                Definition = JsonConvert.SerializeObject(new ImportProcessingJobDefinition() { ResourceLocation = "http://xyz" }),
            };

            var worker2 = new JobInfo()
            {
                Id = 2,
                Status = JobStatus.Completed,
                Result = JsonConvert.SerializeObject(workerResult),
                Definition = JsonConvert.SerializeObject(new ImportProcessingJobDefinition() { ResourceLocation = "http://xyz" }),
            };

            var worker3 = new JobInfo()
            {
                Id = 3,
                Status = JobStatus.Completed,
                Result = JsonConvert.SerializeObject(workerResult),
                Definition = JsonConvert.SerializeObject(new ImportProcessingJobDefinition() { ResourceLocation = "http://xyz2" }),
            };

            var worker4 = new JobInfo()
            {
                Id = 4,
                Status = JobStatus.Running,
            };

            var result = await SetupAndExecuteGetBulkImportJobByIdAsync(coord, [worker1, worker2, worker3, worker4]);

            Assert.Equal(HttpStatusCode.Accepted, result.StatusCode);
            Assert.Equal(2, result.JobResult.Output.Count);
            Assert.Equal(3, result.JobResult.Error.Count);
        }

        [Fact]
        public async Task WhenGettingANotExistingJob_ThenNotFoundShouldBeReturned()
        {
            await Assert.ThrowsAsync<ResourceNotFoundException>(async () => await _mediator.GetImportStatusAsync(1, CancellationToken.None));
        }

        private async Task<GetImportResponse> SetupAndExecuteGetBulkImportJobByIdAsync(JobInfo coord, List<JobInfo> workers)
        {
            _queueClient.GetJobByIdAsync(Arg.Any<byte>(), Arg.Any<long>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(coord);

            var allJobs = new List<JobInfo>(workers);
            allJobs.Add(coord);
            _queueClient.GetJobByGroupIdAsync(Arg.Any<byte>(), Arg.Any<long>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(allJobs);

            return await _mediator.GetImportStatusAsync(coord.Id, CancellationToken.None);
        }
    }
}
