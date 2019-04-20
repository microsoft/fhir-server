// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Export
{
    public class CreateExportRequestHandlerTests
    {
        private readonly IFhirDataStore _fhirDataStore;
        private readonly IMediator _mediator;
        private const string RequestUrl = "https://localhost/$export/";

        public CreateExportRequestHandlerTests()
        {
            _fhirDataStore = Substitute.For<IFhirDataStore>();

            var collection = new ServiceCollection();
            collection.Add(x => new CreateExportRequestHandler(_fhirDataStore)).Singleton().AsSelf().AsImplementedInterfaces();

            ServiceProvider provider = collection.BuildServiceProvider();
            _mediator = new Mediator(type => provider.GetService(type));
        }

        [Fact]
        public async void GivenAFhirMediator_WhenSavingAnExportJobSucceeds_ThenResponseShouldBeSuccess()
        {
            var exportOutcome = new ExportJobOutcome(new ExportJobRecord(new Uri(RequestUrl)), WeakETag.FromVersionId("eTag"));
            _fhirDataStore.CreateExportJobAsync(Arg.Any<ExportJobRecord>(), Arg.Any<CancellationToken>()).Returns(exportOutcome);

            var outcome = await _mediator.ExportAsync(new Uri(RequestUrl));

            Assert.True(outcome.JobCreated);
        }
    }
}
