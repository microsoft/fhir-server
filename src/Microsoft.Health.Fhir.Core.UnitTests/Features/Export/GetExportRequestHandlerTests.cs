// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Threading;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Export
{
    public class GetExportRequestHandlerTests
    {
        private readonly IFhirDataStore _fhirDataStore;
        private readonly IMediator _mediator;
        private const string CreateRequestUrl = "https://localhost/$export/";

        public GetExportRequestHandlerTests()
        {
            _fhirDataStore = Substitute.For<IFhirDataStore>();

            var collection = new ServiceCollection();
            collection.Add(x => new GetExportRequestHandler(_fhirDataStore)).Singleton().AsSelf().AsImplementedInterfaces();

            ServiceProvider provider = collection.BuildServiceProvider();
            _mediator = new Mediator(type => provider.GetService(type));
        }

        [Fact]
        public async void GivenAFhirMediator_WhenGettingAnExistingExportJobWithCompletedStatus_ThenHttpResponseCodeShouldBeOk()
        {
            var jobRecord = new ExportJobRecord(new Uri(CreateRequestUrl));
            jobRecord.Status = OperationStatus.Completed;
            var jobOutcome = new ExportJobOutcome(jobRecord, WeakETag.FromVersionId("eTag"));

            _fhirDataStore.GetExportJobAsync(jobRecord.Id, Arg.Any<CancellationToken>()).Returns(jobOutcome);

            var result = await _mediator.GetExportStatusAsync(new Uri(CreateRequestUrl), jobRecord.Id);

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.NotNull(result.JobResult);

            // Check whether required fields are present.
            Assert.NotNull(result.JobResult.Output);
            Assert.NotEqual(default(DateTimeOffset), result.JobResult.TransactionTime);
            Assert.NotNull(result.JobResult.RequestUri);
        }

        [Fact]
        public async void GivenAFhirMediator_WhenGettingAnExistingExportJobWithNotCompletedStatus_ThenHttpResponseCodeShouldBeAccepted()
        {
            var jobRecord = new ExportJobRecord(new Uri(CreateRequestUrl));
            jobRecord.Status = OperationStatus.Running;
            var jobOutcome = new ExportJobOutcome(jobRecord, WeakETag.FromVersionId("eTag"));

            _fhirDataStore.GetExportJobAsync(jobRecord.Id, Arg.Any<CancellationToken>()).Returns(jobOutcome);

            var result = await _mediator.GetExportStatusAsync(new Uri(CreateRequestUrl), jobRecord.Id);

            Assert.Equal(HttpStatusCode.Accepted, result.StatusCode);
            Assert.Null(result.JobResult);
        }
    }
}
