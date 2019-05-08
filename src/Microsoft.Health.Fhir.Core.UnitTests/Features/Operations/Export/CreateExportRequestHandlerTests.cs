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
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.SecretStore;
using Microsoft.Health.Fhir.Core.Messages.Export;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Export
{
    public class CreateExportRequestHandlerTests
    {
        private readonly IFhirOperationDataStore _fhirOperationDataStore = Substitute.For<IFhirOperationDataStore>();
        private readonly ISecretStore _secretStore = Substitute.For<ISecretStore>();
        private readonly IMediator _mediator;
        private const string RequestUrl = "https://localhost/$export/";
        private const string DestinationType = "destinationType";
        private const string ConnectionString = "destinationConnection";

        public CreateExportRequestHandlerTests()
        {
            var collection = new ServiceCollection();
            collection.Add(x => new CreateExportRequestHandler(_fhirOperationDataStore, _secretStore)).Singleton().AsSelf().AsImplementedInterfaces();

            ServiceProvider provider = collection.BuildServiceProvider();
            _mediator = new Mediator(type => provider.GetService(type));
        }

        [Fact]
        public async void GivenAFhirMediator_WhenSavingAnExportJobSucceeds_ThenResponseShouldBeSuccess()
        {
            var eTag = WeakETag.FromVersionId("eTag");
            _fhirOperationDataStore.CreateExportJobAsync(Arg.Any<ExportJobRecord>(), Arg.Any<CancellationToken>()).Returns(x => new ExportJobOutcome((ExportJobRecord)x[0], eTag));

            CreateExportResponse createResponse = await _mediator.ExportAsync(new Uri(RequestUrl), DestinationType, ConnectionString);

            Assert.NotEmpty(createResponse.JobId);
            await _secretStore.ReceivedWithAnyArgs(1).SetSecretAsync(null, null);
        }
    }
}
