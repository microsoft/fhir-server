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
        public async Task GivenAFhirMediator_WhenGettingAnExistingBulkImportJobWithCompletedStatus_ThenHttpResponseCodeShouldBeOk()
        {
            ImportOrchestratorJobResult orchestratorJobResult = new ImportOrchestratorJobResult()
            {
                TransactionTime = DateTime.Now,
                Request = "Request",
            };

            JobInfo orchestratorJob = new JobInfo()
            {
                Status = JobStatus.Completed,
                Result = JsonConvert.SerializeObject(orchestratorJobResult),
            };

            ImportProcessingJobResult processingJobResult = new ImportProcessingJobResult()
            {
                ResourceLocation = "http://ResourceLocation",
                ResourceType = "Patient",
                SucceedCount = 1,
                FailedCount = 1,
                ErrorLogLocation = "http://ResourceLocation",
            };

            JobInfo processingJob = new JobInfo()
            {
                Status = JobStatus.Completed,
                Result = JsonConvert.SerializeObject(processingJobResult),
            };

            GetImportResponse result = await SetupAndExecuteGetBulkImportJobByIdAsync(orchestratorJob, new List<JobInfo>() { processingJob });

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.Equal(1, result.JobResult.Output.Count);
            Assert.Equal(1, result.JobResult.Error.Count);
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenGettingAnCompletedImportJobWithFailure_ThenHttpResponseCodeShouldBeExpected()
        {
            ImportOrchestratorJobErrorResult orchestratorJobResult = new ImportOrchestratorJobErrorResult()
            {
                HttpStatusCode = HttpStatusCode.BadRequest,
                ErrorMessage = "error",
            };

            JobInfo orchestratorJob = new JobInfo()
            {
                Status = JobStatus.Failed,
                Result = JsonConvert.SerializeObject(orchestratorJobResult),
            };

            OperationFailedException ofe = await Assert.ThrowsAsync<OperationFailedException>(() => SetupAndExecuteGetBulkImportJobByIdAsync(orchestratorJob, new List<JobInfo>()));

            Assert.Equal(HttpStatusCode.BadRequest, ofe.ResponseStatusCode);
            Assert.NotNull(ofe.Message);
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenGettingAnExistingBulkImportJobThatWasCanceled_ThenOperationFailedExceptionIsThrownWithBadRequestHttpResponseCode()
        {
            JobInfo orchestratorJob = new JobInfo()
            {
                Status = JobStatus.Cancelled,
            };
            OperationFailedException ofe = await Assert.ThrowsAsync<OperationFailedException>(() => SetupAndExecuteGetBulkImportJobByIdAsync(orchestratorJob, new List<JobInfo>()));

            Assert.Equal(HttpStatusCode.BadRequest, ofe.ResponseStatusCode);
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenGettingAnExistingBulkImportJobWithNotCompletedStatus_ThenHttpResponseCodeShouldBeAccepted()
        {
            ImportOrchestratorJobResult orchestratorJobResult = new ImportOrchestratorJobResult()
            {
                TransactionTime = DateTime.Now,
                Request = "Request",
            };

            JobInfo orchestratorJob = new JobInfo()
            {
                Id = 1,
                GroupId = 1,
                Status = JobStatus.Running,
                Result = JsonConvert.SerializeObject(orchestratorJobResult),
            };

            ImportProcessingJobResult processingJobResult = new ImportProcessingJobResult()
            {
                ResourceLocation = "http://ResourceLocation",
                ResourceType = "Patient",
                SucceedCount = 1,
                FailedCount = 1,
                ErrorLogLocation = "http://ResourceLocation",
            };

            JobInfo processingJob1 = new JobInfo()
            {
                Id = 2,
                GroupId = 1,
                Status = JobStatus.Completed,
                Result = JsonConvert.SerializeObject(processingJobResult),
            };

            JobInfo processingJob2 = new JobInfo()
            {
                Id = 3,
                GroupId = 1,
                Status = JobStatus.Completed,
                Result = JsonConvert.SerializeObject(processingJobResult),
            };

            JobInfo processingJob3 = new JobInfo()
            {
                Id = 4,
                GroupId = 1,
                Status = JobStatus.Running,
                Result = JsonConvert.SerializeObject(processingJobResult),
            };

            GetImportResponse result = await SetupAndExecuteGetBulkImportJobByIdAsync(orchestratorJob, new List<JobInfo>() { processingJob1, processingJob2, processingJob3 });

            Assert.Equal(HttpStatusCode.Accepted, result.StatusCode);
            Assert.Equal(2, result.JobResult.Output.Count);
            Assert.Equal(2, result.JobResult.Error.Count);
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenGettingWithNotExistJob_ThenNotFoundShouldBeReturned()
        {
            await Assert.ThrowsAsync<ResourceNotFoundException>(async () => await _mediator.GetImportStatusAsync(1, CancellationToken.None));
        }

        private async Task<GetImportResponse> SetupAndExecuteGetBulkImportJobByIdAsync(JobInfo orchestratorJobInfo, List<JobInfo> processingJobInfos)
        {
            _queueClient.GetJobByIdAsync(Arg.Any<byte>(), Arg.Any<long>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(orchestratorJobInfo);

            List<JobInfo> allJobs = new List<JobInfo>(processingJobInfos);
            allJobs.Add(orchestratorJobInfo);
            _queueClient.GetJobByGroupIdAsync(Arg.Any<byte>(), Arg.Any<long>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(allJobs);

            return await _mediator.GetImportStatusAsync(orchestratorJobInfo.Id, CancellationToken.None);
        }
    }
}
