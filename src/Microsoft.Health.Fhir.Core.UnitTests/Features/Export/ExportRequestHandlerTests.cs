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
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Export
{
    public class ExportRequestHandlerTests
    {
        private readonly IDataStore _dataStore;
        private readonly IMediator _mediator;
        private const string RequestUrl = "https://localhost/$export/";

        public ExportRequestHandlerTests()
        {
            _dataStore = Substitute.For<IDataStore>();

            var collection = new ServiceCollection();
            collection.Add(x => new ExportRequestHandler(_dataStore)).Singleton().AsSelf().AsImplementedInterfaces();

            ServiceProvider provider = collection.BuildServiceProvider();
            _mediator = new Mediator(type => provider.GetService(type));
        }

        [Fact]
        public async void GivenAFhirMediator_WhenSavingAnExportJobSucceeds_ThenResponseShouldBeSuccesful()
        {
            _dataStore.UpsertExportJobAsync(Arg.Any<ExportJobRecord>())
                .Returns(x => JobCreationStatus.Created);

            var outcome = await _mediator.ExportAsync(new Uri(RequestUrl));

            Assert.Equal(JobCreationStatus.Created, outcome.JobStatus);
        }

        [Fact]
        public async void GivenAFhirMediator_WhenSavingAnExportJobFails_ThenResponseShouldBeFailed()
        {
            _dataStore.UpsertExportJobAsync(Arg.Any<ExportJobRecord>())
                .Returns(x => JobCreationStatus.Failed);

            var outcome = await _mediator.ExportAsync(new Uri(RequestUrl));

            Assert.Equal(JobCreationStatus.Failed, outcome.JobStatus);
        }
    }
}
