// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Messages.SearchParameterState;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Controllers
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.SearchParameterStatus)]
    public class SearchParameterControllerTests
    {
        private readonly CoreFeatureConfiguration _coreFeaturesConfiguration = new CoreFeatureConfiguration();
        private readonly IMediator _mediator = Substitute.For<IMediator>();
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
        private readonly SearchParameterController _controller;
        private HttpContext _httpContext = new DefaultHttpContext();

        public SearchParameterControllerTests()
        {
            var controllerContext = new ControllerContext() { HttpContext = _httpContext };
            _coreFeaturesConfiguration.SupportsSelectiveSearchParameters = true;
            _controller = new SearchParameterController(
                _mediator,
                Options.Create(_coreFeaturesConfiguration));
            _controller.ControllerContext = controllerContext;
        }

        [Fact]
        public async void GivenASearchParameterStatusRequest_WhenSupportsSelectiveSearchParametersFlagIsFalse_ThenRequestNotValidExceptionShouldBeReturned()
        {
            CoreFeatureConfiguration coreFeaturesConfiguration = new CoreFeatureConfiguration();
            coreFeaturesConfiguration.SupportsSelectiveSearchParameters = false;

            SearchParameterController controller = new SearchParameterController(_mediator, Options.Create(coreFeaturesConfiguration));

            Func<System.Threading.Tasks.Task> act = () => controller.GetSearchParametersStatus(default(CancellationToken));

            var exception = await Assert.ThrowsAsync<RequestNotValidException>(act);
        }

        [Fact]
        public async void GivenASearchParameterStatusRequest_WhenSupportsSelectiveSearchParametersFlagIsTrue_ThenMediatorShouldBeCalled()
        {
            try
            {
                await _controller.GetSearchParametersStatus(default(CancellationToken));
            }
            catch
            {
            }

            await _mediator.Received(1).Send(Arg.Any<SearchParameterStateRequest>(), default(CancellationToken));
        }

        /*
        [Fact]
        public async void GivenASearchParameterStatusRequest_WhenMediatorReturnsSearchParameterStatusResponse_ThenOkObjectResultShouldBeReturned()
        {
            var controllerContext = new ControllerContext() { HttpContext = _httpContext };
            ParameterInfo parameterInfo = new ParameterInfo() { }
            var response = new ResourceElement(;

            _mediator.Send(Arg.Any<SearchParameterStateRequest>(), default(CancellationToken)).Returns(new SearchParameterStateResponse();
            var result = await _controller.GetSearchParametersStatus(default(CancellationToken));
            Assert.IsType<OkObjectResult>(result);
        }
        */
    }
}
