// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Messages.Everything;
using Microsoft.Health.Fhir.Core.Models;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.UnitTests.Controllers
{
    public class EverythingControllerTests
    {
        private readonly EverythingController _everythingController;
        private readonly IMediator _mediator = Substitute.For<IMediator>();
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();

        public EverythingControllerTests()
        {
            _everythingController = GetController();
            _everythingController.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            };
        }

        [Fact]
        public async Task GivenAnEverythingOperationRequest_WhenValid_ThenProperResponseShouldBeReturned()
        {
            _mediator.Send(Arg.Any<EverythingOperationRequest>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(GetEverythingOperationResponse()));

            var result = await _everythingController.PatientEverythingById(idParameter: "123") as FhirResult;

            await _mediator.Received().Send(
                Arg.Is<EverythingOperationRequest>(r =>
                    string.Equals(r.EverythingOperationType, ResourceType.Patient.ToString(), StringComparison.Ordinal) &&
                    string.Equals(r.ResourceId.ToString(), "123", StringComparison.OrdinalIgnoreCase)),
                Arg.Any<CancellationToken>());

            _mediator.ClearReceivedCalls();

            var bundleResource = (result?.Result as ResourceElement)?.ResourceInstance as Bundle;
            Assert.Equal(System.Net.HttpStatusCode.OK, result?.StatusCode);
            Assert.Equal(Bundle.BundleType.Searchset, bundleResource?.Type);
        }

        private EverythingController GetController()
        {
            var featureConfig = new FeatureConfiguration();

            IOptions<FeatureConfiguration> optionsFeatureConfiguration = Substitute.For<IOptions<FeatureConfiguration>>();
            optionsFeatureConfiguration.Value.Returns(featureConfig);

            return new EverythingController(_mediator, _fhirRequestContextAccessor);
        }

        private static EverythingOperationResponse GetEverythingOperationResponse()
        {
            var bundle = new Bundle
            {
                Type = Bundle.BundleType.Searchset,
            };

            return new EverythingOperationResponse(bundle.ToResourceElement());
        }
    }
}
