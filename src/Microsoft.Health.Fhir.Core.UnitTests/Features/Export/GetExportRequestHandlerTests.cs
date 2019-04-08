// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Export;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Export;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Export
{
    public class GetExportRequestHandlerTests
    {
        private readonly IDataStore _dataStore;
        private readonly IMediator _mediator;
        private const string CreateRequestUrl = "https://localhost/$export/";

        public GetExportRequestHandlerTests()
        {
            _dataStore = Substitute.For<IDataStore>();

            var collection = new ServiceCollection();
            collection.Add(x => new GetExportRequestHandler(_dataStore)).Singleton().AsSelf().AsImplementedInterfaces();

            ServiceProvider provider = collection.BuildServiceProvider();
            _mediator = new Mediator(type => provider.GetService(type));
        }

        [Fact]
        public async void GivenAFhirMediator_WhenGettingANonExistingExportJob_ThenHttpResponseShouldBeNotFound()
        {
            ExportJobRecord jobRecord = null;
            _dataStore.GetExportJobAsync(Arg.Any<string>())
                .Returns(x => jobRecord);

            var result = await _mediator.GetExportStatusAsync(new Uri(CreateRequestUrl), "id");

            Assert.False(result.JobExists);
            Assert.Equal(HttpStatusCode.NotFound, result.StatusCode);
        }

        [Fact]
        public async void GivenAFhirMediator_WhenGettingAnExistingExportJobWithCompletedStatus_ThenHttpResponseCodeShouldBeOk()
        {
            var jobRecord = new ExportJobRecord(new CreateExportRequest(new Uri(CreateRequestUrl)), 1);
            jobRecord.JobStatus = OperationStatus.Completed;

            _dataStore.GetExportJobAsync(Arg.Any<string>())
                .Returns(x => jobRecord);

            var result = await _mediator.GetExportStatusAsync(new Uri(CreateRequestUrl), jobRecord.Id);

            Assert.True(result.JobExists);
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.NotNull(result.JobResult);
        }

        [Fact]
        public async void GivenAFhirMediator_WhenGettingAnExistingExportJobWithNotCompletedStatus_ThenHttpResponseCodeShouldBeAccepted()
        {
            var jobRecord = new ExportJobRecord(new CreateExportRequest(new Uri(CreateRequestUrl)), 1);
            jobRecord.JobStatus = OperationStatus.Running;

            _dataStore.GetExportJobAsync(Arg.Any<string>())
                .Returns(x => jobRecord);

            var result = await _mediator.GetExportStatusAsync(new Uri(CreateRequestUrl), jobRecord.Id);

            Assert.True(result.JobExists);
            Assert.Equal(HttpStatusCode.Accepted, result.StatusCode);
            Assert.Null(result.JobResult);
        }
    }
}
