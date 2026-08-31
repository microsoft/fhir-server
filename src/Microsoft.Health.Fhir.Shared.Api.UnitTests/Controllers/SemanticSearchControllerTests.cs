// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using Hl7.Fhir.Model;
using Medino;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Messages.SemanticSearch;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.UnitTests.Controllers
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SemanticSearchControllerTests
    {
        private readonly IMediator _mediator = Substitute.For<IMediator>();
        private readonly SemanticSearchController _controller;

        public SemanticSearchControllerTests()
        {
            _controller = new SemanticSearchController(
                _mediator,
                Options.Create(new VectorSearchConfiguration()))
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext(),
                },
            };
        }

        [Fact]
        public async Task GivenPatientSemanticSearch_WhenValid_ThenPatientIdComesFromRoute()
        {
            _mediator.SendAsync<SemanticSearchResponse>(Arg.Any<SemanticSearchRequest>(), Arg.Any<CancellationToken>())
                .Returns(new SemanticSearchResponse(new Bundle { Type = Bundle.BundleType.Searchset }.ToResourceElement()));
            var parameters = new Parameters
            {
                Parameter =
                {
                    new Parameters.ParameterComponent { Name = "query", Value = new FhirString("breathing difficulty") },
                    new Parameters.ParameterComponent { Name = "count", Value = new Integer(3) },
                    new Parameters.ParameterComponent { Name = "type", Value = new Code("Observation") },
                    new Parameters.ParameterComponent { Name = "type", Value = new Code("DiagnosticReport") },
                    new Parameters.ParameterComponent { Name = "patient", Value = new ResourceReference("Patient/ignored") },
                },
            };

            var result = await _controller.Search("123", parameters) as FhirResult;

            Assert.NotNull(result);
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            await _mediator.Received(1).SendAsync<SemanticSearchResponse>(
                Arg.Is<SemanticSearchRequest>(request =>
                    request.Query == "breathing difficulty" &&
                    request.PatientId == "123" &&
                    request.Count == 3 &&
                    request.ResourceTypes.SequenceEqual(new[] { "Observation", "DiagnosticReport" })),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenPatientSemanticSearchWithoutCount_WhenValid_ThenDefaultCountIsUsed()
        {
            _mediator.SendAsync<SemanticSearchResponse>(Arg.Any<SemanticSearchRequest>(), Arg.Any<CancellationToken>())
                .Returns(new SemanticSearchResponse(new Bundle { Type = Bundle.BundleType.Searchset }.ToResourceElement()));
            var parameters = new Parameters
            {
                Parameter =
                {
                    new Parameters.ParameterComponent { Name = "query", Value = new FhirString("breathing difficulty") },
                },
            };

            await _controller.Search("123", parameters);

            await _mediator.Received(1).SendAsync<SemanticSearchResponse>(
                Arg.Is<SemanticSearchRequest>(request => request.Count == 10),
                Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        public async Task GivenPatientSemanticSearchWithoutQuery_WhenHandled_ThenRequestIsRejected(string query)
        {
            var parameters = new Parameters
            {
                Parameter =
                {
                    new Parameters.ParameterComponent { Name = "query", Value = new FhirString(query) },
                },
            };

            await Assert.ThrowsAsync<RequestNotValidException>(() => _controller.Search("123", parameters));
            await _mediator.DidNotReceive().SendAsync<SemanticSearchResponse>(Arg.Any<SemanticSearchRequest>(), Arg.Any<CancellationToken>());
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(51)]
        public async Task GivenPatientSemanticSearchWithInvalidCount_WhenHandled_ThenRequestIsRejected(int count)
        {
            var parameters = new Parameters
            {
                Parameter =
                {
                    new Parameters.ParameterComponent { Name = "query", Value = new FhirString("breathing difficulty") },
                    new Parameters.ParameterComponent { Name = "count", Value = new Integer(count) },
                },
            };

            await Assert.ThrowsAsync<RequestNotValidException>(() => _controller.Search("123", parameters));
            await _mediator.DidNotReceive().SendAsync<SemanticSearchResponse>(Arg.Any<SemanticSearchRequest>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenPatientSemanticSearchWithResourceType_WhenHandled_ThenTypeIsForwardedForMetadataValidation()
        {
            _mediator.SendAsync<SemanticSearchResponse>(Arg.Any<SemanticSearchRequest>(), Arg.Any<CancellationToken>())
                .Returns(new SemanticSearchResponse(new Bundle { Type = Bundle.BundleType.Searchset }.ToResourceElement()));
            var parameters = new Parameters
            {
                Parameter =
                {
                    new Parameters.ParameterComponent { Name = "query", Value = new FhirString("breathing difficulty") },
                    new Parameters.ParameterComponent { Name = "type", Value = new Code("Condition") },
                },
            };

            await _controller.Search("123", parameters);

            await _mediator.Received(1).SendAsync<SemanticSearchResponse>(
                Arg.Is<SemanticSearchRequest>(request => request.ResourceTypes.SequenceEqual(new[] { "Condition" })),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public void GivenSemanticSearchController_WhenInspectingRoute_ThenPatientInstanceOperationIsExposed()
        {
            RouteAttribute route = typeof(SemanticSearchController)
                .GetMethod(nameof(SemanticSearchController.Search), BindingFlags.Instance | BindingFlags.Public)
                .GetCustomAttributes<RouteAttribute>()
                .Single();

            Assert.Equal("Patient/{idParameter}/$semantic-search", route.Template);
        }
    }
}
