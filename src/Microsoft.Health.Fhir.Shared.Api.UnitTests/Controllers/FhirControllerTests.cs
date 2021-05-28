// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Messages.Everything;
using Microsoft.Health.Fhir.Core.Models;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.UnitTests.Controllers
{
    public class FhirControllerTests
    {
        private readonly FhirController _fhirController;
        private readonly IMediator _mediator = Substitute.For<IMediator>();
        private readonly ILogger<FhirController> _logger = Substitute.For<ILogger<FhirController>>();
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
        private readonly IUrlResolver _urlResolver = Substitute.For<IUrlResolver>();
        private readonly IAuthorizationService _authorizationService = Substitute.For<IAuthorizationService>();
        private readonly HttpContext _httpContext = new DefaultHttpContext();

        public FhirControllerTests()
        {
            _fhirController = GetController();
            var controllerContext = new ControllerContext { HttpContext = _httpContext };
            _fhirController.ControllerContext = controllerContext;
        }

        [Fact]
        public async Task GivenAnEverythingOperationRequest_WhenValid_ThenProperResponseShouldBeReturned()
        {
            _mediator.Send(Arg.Any<EverythingOperationRequest>()).Returns(Task.FromResult(GetEverythingOperationResponse()));

            IActionResult result = await _fhirController.PatientEverythingById(
                idParameter: "123",
                start: PartialDateTime.Parse("2019"),
                end: PartialDateTime.Parse("2020"),
                since: PartialDateTime.Parse("2021"),
                type: ResourceType.Observation.ToString(),
                ct: null);

            await _mediator.Received().Send(
                Arg.Is<EverythingOperationRequest>(
                    r => string.Equals(r.EverythingOperationType, ResourceType.Patient.ToString(), StringComparison.Ordinal)
                         && string.Equals(r.ResourceId.ToString(), "123", StringComparison.OrdinalIgnoreCase)
                         && string.Equals(r.Start.ToString(), "2019", StringComparison.Ordinal)
                         && string.Equals(r.End.ToString(), "2020", StringComparison.Ordinal)
                         && string.Equals(r.Since.ToString(), "2021", StringComparison.Ordinal)
                         && string.Equals(r.ResourceTypes, ResourceType.Observation.ToString(), StringComparison.Ordinal)
                         && r.ContinuationToken == null),
                Arg.Any<CancellationToken>());

            _mediator.ClearReceivedCalls();

            var bundleResource = (((FhirResult)result).Result as ResourceElement)?.ResourceInstance as Bundle;
            Assert.Equal(Bundle.BundleType.Searchset, bundleResource?.Type);
        }

        private FhirController GetController()
        {
            var featureConfig = new FeatureConfiguration();

            IOptions<FeatureConfiguration> optionsFeatureConfiguration = Substitute.For<IOptions<FeatureConfiguration>>();
            optionsFeatureConfiguration.Value.Returns(featureConfig);

            return new FhirController(_mediator, _logger, _fhirRequestContextAccessor, _urlResolver, optionsFeatureConfiguration, _authorizationService);
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
