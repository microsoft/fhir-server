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
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Messages.Everything;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.UnitTests.Controllers
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class EverythingControllerTests
    {
        private readonly EverythingController _everythingController;
        private readonly IMediator _mediator = Substitute.For<IMediator>();
        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();

        public EverythingControllerTests()
        {
            _everythingController = new EverythingController(_mediator, _fhirRequestContextAccessor)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext(),
                },
            };
        }

        [Fact]
        public async Task GivenAnEverythingOperationRequest_WhenValid_ThenProperResponseShouldBeReturned()
        {
            _mediator.Send(Arg.Any<EverythingOperationRequest>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(GetEverythingOperationResponse()));

            var result = await _everythingController.PatientEverythingById(
                idParameter: "123",
                start: PartialDateTime.Parse("2019"),
                end: PartialDateTime.Parse("2020"),
                since: PartialDateTime.Parse("2021"),
                type: ResourceType.Observation.ToString(),
                ct: null) as FhirResult;

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

            var bundleResource = (result?.Result as ResourceElement)?.ResourceInstance as Bundle;
            Assert.Equal(System.Net.HttpStatusCode.OK, result?.StatusCode);
            Assert.Equal(Bundle.BundleType.Searchset, bundleResource?.Type);
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
