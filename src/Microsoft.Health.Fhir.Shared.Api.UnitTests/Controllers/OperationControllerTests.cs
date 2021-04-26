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
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Messages.Operation;
using Microsoft.Health.Fhir.Core.Models;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.UnitTests.Controllers
{
    public class OperationControllerTests
    {
        private readonly OperationController _operationController;
        private readonly IMediator _mediator = Substitute.For<IMediator>();
        private readonly HttpContext _httpContext = new DefaultHttpContext();

        public OperationControllerTests()
        {
            _operationController = GetController();
            var controllerContext = new ControllerContext { HttpContext = _httpContext };
            _operationController.ControllerContext = controllerContext;
        }

        [Fact]
        public async Task GivenAnEverythingOperationRequest_WhenValid_ThenProperResponseShouldBeReturned()
        {
            _mediator.Send(Arg.Any<EverythingOperationRequest>()).Returns(Task.FromResult(GetEverythingOperationResponse()));

            IActionResult result = await _operationController.EverythingById(
                typeParameter: ResourceType.Patient.ToString(),
                idParameter: "123",
                start: PartialDateTime.Parse("2019"),
                end: PartialDateTime.Parse("2020"),
                since: PartialDateTime.Parse("2021"),
                type: ResourceType.Observation.ToString(),
                count: null,
                ct: null);

            await _mediator.Received().Send(
                Arg.Is<EverythingOperationRequest>(
                    r => string.Equals(r.ResourceType, ResourceType.Patient.ToString(), StringComparison.Ordinal)
                         && string.Equals(r.ResourceId.ToString(), "123", StringComparison.OrdinalIgnoreCase)
                         && string.Equals(r.Start.ToString(), "2019", StringComparison.Ordinal)
                         && string.Equals(r.End.ToString(), "2020", StringComparison.Ordinal)
                         && string.Equals(r.Since.ToString(), "2021", StringComparison.Ordinal)
                         && string.Equals(r.Type, ResourceType.Observation.ToString(), StringComparison.Ordinal)
                         && r.Count == null
                         && r.ContinuationToken == null),
                Arg.Any<CancellationToken>());

            _mediator.ClearReceivedCalls();

            var bundleResource = (((FhirResult)result).Result as ResourceElement)?.ResourceInstance as Bundle;
            Assert.Equal(Bundle.BundleType.Searchset, bundleResource?.Type);
        }

        [Fact]
        public async Task GivenAnEverythingOperationRequest_WhenResourceTypeIsNotPatient_ThenRequestNotValidExceptionShouldBeThrown()
        {
            await Assert.ThrowsAsync<RequestNotValidException>(() => _operationController.EverythingById(
                type: ResourceType.Observation.ToString(),
                idParameter: null,
                typeParameter: null,
                start: null,
                end: null,
                since: null,
                count: null,
                ct: null));
        }

        private OperationController GetController()
        {
            return new(_mediator);
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
