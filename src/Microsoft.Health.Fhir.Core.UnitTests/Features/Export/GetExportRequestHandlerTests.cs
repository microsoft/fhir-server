// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Export;
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
        private readonly Uri _requestUri = new Uri("https://localhost/$export/");

        public GetExportRequestHandlerTests()
        {
            _dataStore = Substitute.For<IDataStore>();

            var collection = new ServiceCollection();
            collection.Add(x => new GetExportRequestHandler(_dataStore)).Singleton().AsSelf().AsImplementedInterfaces();

            ServiceProvider provider = collection.BuildServiceProvider();
            _mediator = new Mediator(type => provider.GetService(type));
        }

        [Fact]
        public async void GivenAFhirMediator_WhenGettingAnExistingExportJob_ThenResponseShouldBeSuccesful()
        {
            var exportRequest = new CreateExportRequest(_requestUri);
            var jobRecord = new ExportJobRecord(exportRequest, 1);

            _dataStore.GetExportJobAsync(Arg.Any<string>())
                .Returns(x => jobRecord);

            var outcome = await _mediator.GetExportStatusAsync(_requestUri, jobRecord.Id);

            Assert.True(outcome.JobExists);
        }

        [Fact]
        public async void GivenAFhirMediator_WhenGettingANonExistingExportJob_ThenResponseShouldBeSuccesful()
        {
            ExportJobRecord jobRecord = null;
            _dataStore.GetExportJobAsync(Arg.Any<string>())
                .Returns(x => jobRecord);

            var outcome = await _mediator.GetExportStatusAsync(_requestUri, "id");

            Assert.False(outcome.JobExists);
        }
    }
}
