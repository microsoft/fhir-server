// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
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
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Messages.Export;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Export
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Export)]
    public class GetExportRequestHandlerTests
    {
        private readonly IFhirOperationDataStore _fhirOperationDataStore = Substitute.For<IFhirOperationDataStore>();
        private readonly IMediator _mediator;

        private readonly Uri _createRequestUri = new Uri("https://localhost/$export/");
        private const string _failureReason = "ExportJobFailed";
        private HttpStatusCode _failureStatusCode = HttpStatusCode.InternalServerError;

        public GetExportRequestHandlerTests()
        {
            var collection = new ServiceCollection();
            collection.Add(x => new GetExportRequestHandler(_fhirOperationDataStore, DisabledFhirAuthorizationService.Instance)).Singleton().AsSelf().AsImplementedInterfaces();

            ServiceProvider provider = collection.BuildServiceProvider();
            _mediator = new Mediator(provider);
        }

        [Theory]
        [InlineData(OperationStatus.Completed)]
        [InlineData(OperationStatus.Canceled)]
        public async Task GivenAFhirMediator_WhenGettingAnExistingExportJobWithCompletedOrCancelledStatus_ThenHttpResponseCodeShouldBeOk(OperationStatus operationStatus)
        {
            GetExportResponse result = await SetupAndExecuteGetExportJobByIdAsync(operationStatus);

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.NotNull(result.JobResult);

            // Check whether required fields are present.
            Assert.NotNull(result.JobResult.Output);
            Assert.NotEqual(default, result.JobResult.TransactionTime);
            Assert.NotNull(result.JobResult.RequestUri);
            Assert.NotNull(result.JobResult.Error);
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenGettingAnExistingExportJobWithCompletedStatus_ThenOutputShouldContainRequiredFields()
        {
            GetExportResponse result = await SetupAndExecuteGetExportJobByIdAsync(OperationStatus.Completed);

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.NotNull(result.JobResult);

            var output = result.JobResult.Output.FirstOrDefault();

            // Check whether required fields are present for Output.
            Assert.NotNull(output);
            Assert.False(string.IsNullOrWhiteSpace(output.Type));
            Assert.NotNull(output.FileUri);
            Assert.True(output.Count >= 0);

            var error = result.JobResult.Error.FirstOrDefault();

            // Check whether required fields are present for Error.
            Assert.NotNull(error);
            Assert.False(string.IsNullOrWhiteSpace(error.Type));
            Assert.NotNull(error.FileUri);
            Assert.True(error.Count >= 0);

            var issue = result.JobResult.Issues.FirstOrDefault();

            // Check whether required fields are present for Issues.
            Assert.NotNull(issue);
            Assert.False(string.IsNullOrWhiteSpace(issue.Diagnostics));
            Assert.False(string.IsNullOrWhiteSpace(issue.Code));
            Assert.False(string.IsNullOrWhiteSpace(issue.Severity));
        }

        [Theory]
        [InlineData(OperationStatus.Failed, HttpStatusCode.BadRequest)]
        [InlineData(OperationStatus.Failed, HttpStatusCode.InternalServerError)]
        public async Task GivenAFhirMediator_WhenGettingAnExistingExportJobWithFailedStatus_ThenOperationFailedExceptionIsThrownWithCorrectHttpResponseCode(OperationStatus operationStatus, HttpStatusCode failureStatusCode)
        {
            _failureStatusCode = failureStatusCode;

            OperationFailedException ofe = await Assert.ThrowsAsync<OperationFailedException>(() => SetupAndExecuteGetExportJobByIdAsync(operationStatus));

            Assert.NotNull(ofe);
            Assert.Equal(failureStatusCode, ofe.ResponseStatusCode);
            Assert.Contains(_failureReason, ofe.Message);
        }

        [Theory]
        [InlineData(OperationStatus.Running)]
        [InlineData(OperationStatus.Queued)]
        public async Task GivenAFhirMediator_WhenGettingAnExistingExportJobWithNotCompletedStatus_ThenHttpResponseCodeShouldBeAccepted(OperationStatus operationStatus)
        {
            GetExportResponse result = await SetupAndExecuteGetExportJobByIdAsync(operationStatus);

            Assert.Equal(HttpStatusCode.Accepted, result.StatusCode);
            Assert.Null(result.JobResult);
        }

        [Theory]
        [InlineData(OperationStatus.Failed)]
        public async Task GivenAFhirMediator_WhenGettingAnExistingExportJobWithFailedStatusAndNoFailureDetails_ThenOperationFailedExceptionIsThrownWithCorrectHttpResponseCode(OperationStatus operationStatus)
        {
            _failureStatusCode = HttpStatusCode.BadRequest;

            OperationFailedException ofe = await Assert.ThrowsAsync<OperationFailedException>(() => SetupAndExecuteGetExportJobByIdAsync(operationStatus, addFailureDetails: false));

            Assert.NotNull(ofe);
            Assert.Equal(HttpStatusCode.InternalServerError, ofe.ResponseStatusCode);
            Assert.Contains(Resources.UnknownError, ofe.Message);
        }

        private async Task<GetExportResponse> SetupAndExecuteGetExportJobByIdAsync(OperationStatus jobStatus, bool addFailureDetails = true)
        {
            var jobRecord = new ExportJobRecord(_createRequestUri, ExportJobType.Patient, ExportFormatTags.ResourceName, null, null, "hash", rollingFileSizeInMB: 64)
            {
                Status = jobStatus,
            };

            if ((jobStatus == OperationStatus.Canceled || jobStatus == OperationStatus.Failed) && addFailureDetails)
            {
                jobRecord.FailureDetails = new JobFailureDetails(_failureReason, _failureStatusCode);
            }
            else if (jobStatus == OperationStatus.Completed)
            {
                var exportFileInfo = new ExportFileInfo("patient", new Uri("https://exportlocation/fileUri"), sequence: 0);
                exportFileInfo.IncrementCount(100);
                jobRecord.Output.Add("patient", new List<ExportFileInfo>() { exportFileInfo });

                var exportErrorInfo = new ExportFileInfo("error", new Uri("https://exportlocation/fileUri"), sequence: 0);
                exportErrorInfo.IncrementCount(100);
                jobRecord.Error.Add(exportErrorInfo);

                var exportIssue = new OperationOutcomeIssue("warning", "code", "message");
                jobRecord.Issues.Add(exportIssue);
            }

            var jobOutcome = new ExportJobOutcome(jobRecord, WeakETag.FromVersionId("eTag"));

            _fhirOperationDataStore.GetExportJobByIdAsync(jobRecord.Id, Arg.Any<CancellationToken>()).Returns(jobOutcome);

            return await _mediator.GetExportStatusAsync(_createRequestUri, jobRecord.Id, CancellationToken.None);
        }
    }
}
