﻿// -------------------------------------------------------------------------------------------------
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
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Export;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Export
{
    public class GetExportRequestHandlerTests
    {
        private readonly IFhirOperationDataStore _fhirOperationDataStore = Substitute.For<IFhirOperationDataStore>();
        private readonly IMediator _mediator;
        private readonly Uri _createRequestUri = new Uri("https://localhost/$export/");
        private const string _failureReason = "ExportJobFailed";

        public GetExportRequestHandlerTests()
        {
            var collection = new ServiceCollection();
            collection.Add(x => new GetExportRequestHandler(_fhirOperationDataStore)).Singleton().AsSelf().AsImplementedInterfaces();

            ServiceProvider provider = collection.BuildServiceProvider();
            _mediator = new Mediator(type => provider.GetService(type));
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenGettingAnExistingExportJobWithCompletedStatus_ThenHttpResponseCodeShouldBeOk()
        {
            GetExportResponse result = await SetupAndExecuteGetExportJobByIdAsync(OperationStatus.Completed);

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.NotNull(result.JobResult);

            // Check whether required fields are present.
            Assert.NotNull(result.JobResult.Output);
            Assert.NotEqual(default, result.JobResult.TransactionTime);
            Assert.NotNull(result.JobResult.RequestUri);
            Assert.NotNull(result.JobResult.Error);
        }

        [Theory]
        [InlineData(OperationStatus.Canceled)]
        [InlineData(OperationStatus.Failed)]
        public async Task GivenAFhirMediator_WhenGettingAnExistingExportJobWithFailedStatus_ThenHttpResponseCodeShouldBeServerError(OperationStatus operationStatus)
        {
            GetExportResponse result = await SetupAndExecuteGetExportJobByIdAsync(operationStatus);

            Assert.Equal(HttpStatusCode.InternalServerError, result.StatusCode);
            Assert.Equal(_failureReason, result.FailureReason);
            Assert.Null(result.JobResult);
        }

        [Theory]
        [InlineData(OperationStatus.Running)]
        [InlineData(OperationStatus.Queued)]
        public async Task GivenAFhirMediator_WhenGettingAnExistingExportJobWithNotCompletedStatus_ThenHttpResponseCodeShouldBeAccepted(OperationStatus operationStatus)
        {
            GetExportResponse result = await SetupAndExecuteGetExportJobByIdAsync(operationStatus);

            Assert.Equal(HttpStatusCode.Accepted, result.StatusCode);
            Assert.Null(result.JobResult);
            Assert.Null(result.FailureReason);
        }

        private async Task<GetExportResponse> SetupAndExecuteGetExportJobByIdAsync(OperationStatus jobStatus)
        {
            var jobRecord = new ExportJobRecord(_createRequestUri, "Patient", "hash")
            {
                Status = jobStatus,
            };

            if (jobStatus == OperationStatus.Canceled || jobStatus == OperationStatus.Failed)
            {
                jobRecord.FailureReason = _failureReason;
            }

            var jobOutcome = new ExportJobOutcome(jobRecord, WeakETag.FromVersionId("eTag"));

            _fhirOperationDataStore.GetExportJobByIdAsync(jobRecord.Id, Arg.Any<CancellationToken>()).Returns(jobOutcome);

            return await _mediator.GetExportStatusAsync(_createRequestUri, jobRecord.Id, CancellationToken.None);
        }
    }
}
