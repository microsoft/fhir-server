// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Export;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Export
{
    public class CreateExportRequestHandlerTests
    {
        private static readonly Uri RequestUrl = new Uri("https://localhost/$export/");

        private readonly IClaimsExtractor _claimsExtractor = Substitute.For<IClaimsExtractor>();
        private readonly IFhirOperationDataStore _fhirOperationDataStore = Substitute.For<IFhirOperationDataStore>();
        private readonly CreateExportRequestHandler _createExportRequestHandler;

        private readonly CancellationToken _cancellationToken = new CancellationTokenSource().Token;

        public CreateExportRequestHandlerTests()
        {
            _createExportRequestHandler = new CreateExportRequestHandler(_claimsExtractor, _fhirOperationDataStore);
        }

        [Fact]
        public async void GivenThereIsNoMatchingJob_WhenCreatingAnExportJob_ThenNewJobShouldBeCreated()
        {
            _fhirOperationDataStore.GetExportJobByHashAsync(Arg.Any<string>(), _cancellationToken).Returns(Task.FromResult<ExportJobOutcome>(null));

            _fhirOperationDataStore.CreateExportJobAsync(Arg.Any<ExportJobRecord>(), Arg.Any<CancellationToken>()).Returns(x => new ExportJobOutcome(x.ArgAt<ExportJobRecord>(0), WeakETag.FromVersionId("eTag")));

            var request = new CreateExportRequest(RequestUrl);

            CreateExportResponse response = await _createExportRequestHandler.Handle(request, _cancellationToken);

            Assert.NotNull(response);
            Assert.NotEmpty(response.JobId);
        }

        [Fact]
        public async void GivenThereIsAMatchingJob_WhenCreatingAnExportJob_ThenNewJobShouldBeCreated()
        {
            var exportJobRecord = new ExportJobRecord(RequestUrl);
            var exportJobOutcome = new ExportJobOutcome(exportJobRecord, WeakETag.FromVersionId("eTag"));

            _fhirOperationDataStore.GetExportJobByHashAsync(Arg.Any<string>(), _cancellationToken).Returns(Task.FromResult(exportJobOutcome));

            var request = new CreateExportRequest(RequestUrl);

            var response = await _createExportRequestHandler.Handle(request, _cancellationToken);

            Assert.NotNull(response);
            Assert.Equal(exportJobRecord.Id, response.JobId);

            await _fhirOperationDataStore.DidNotReceiveWithAnyArgs().CreateExportJobAsync(null, CancellationToken.None);
        }
    }
}
