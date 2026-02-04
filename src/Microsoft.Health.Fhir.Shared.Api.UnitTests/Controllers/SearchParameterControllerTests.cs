// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations.SearchParameterState;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Messages.SearchParameterState;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using static Hl7.Fhir.Model.Parameters;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.UnitTests.Controllers
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.SearchParameterStatus)]
    public class SearchParameterControllerTests
    {
        private readonly CoreFeatureConfiguration _coreFeaturesConfiguration = new CoreFeatureConfiguration();
        private readonly IFhirRuntimeConfiguration _fhirConfiguration = new AzureHealthDataServicesRuntimeConfiguration();
        private readonly IMediator _mediator = Substitute.For<IMediator>();
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
        private readonly SearchParameterController _controller;
        private HttpContext _httpContext = new DefaultHttpContext();
        private static readonly string DummyUrl = "http://someresouce";
        private static readonly string InvalidStatus = "invalidStatus";

        public SearchParameterControllerTests()
        {
            var controllerContext = new ControllerContext() { HttpContext = _httpContext };
            _coreFeaturesConfiguration.SupportsSelectableSearchParameters = true;
            _mediator.Send(Arg.Any<SearchParameterStateRequest>(), Arg.Any<CancellationToken>()).Returns(new SearchParameterStateResponse());
            _mediator.Send(Arg.Any<SearchParameterStateUpdateRequest>(), Arg.Any<CancellationToken>()).Returns(new SearchParameterStateUpdateResponse());
            _controller = new SearchParameterController(
                _mediator,
                Options.Create(_coreFeaturesConfiguration),
                _fhirConfiguration);
            _controller.ControllerContext = controllerContext;
        }

        [Fact]
        public async Task GivenASearchParameterStatusRequest_WhenSupportsSelectableSearchParametersFlagIsFalse_ThenRequestNotValidExceptionShouldBeReturned()
        {
            CoreFeatureConfiguration coreFeaturesConfiguration = new CoreFeatureConfiguration();
            coreFeaturesConfiguration.SupportsSelectableSearchParameters = false;

            SearchParameterController controller = new SearchParameterController(_mediator, Options.Create(coreFeaturesConfiguration), _fhirConfiguration);

            Func<Task> act = () => controller.GetSearchParametersStatus(TestContext.Current.CancellationToken);

            var exception = await Assert.ThrowsAsync<RequestNotValidException>(act);
        }

        [Fact]
        public async Task GivenASearchParameterStatusRequest_WhenSupportsSelectableSearchParametersFlagIsTrue_ThenMediatorShouldBeCalled()
        {
            try
            {
                await _controller.GetSearchParametersStatus(TestContext.Current.CancellationToken);
            }
            catch
            {
            }

            await _mediator.Received(1).Send(Arg.Any<SearchParameterStateRequest>(), TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task GivenAnInvalidUpdateRequestBody_WhenParsingRequestBody_RequestNotValidExceptionIsThrown()
        {
            var requestBody = CreateInvalidRequestBody();

            Func<System.Threading.Tasks.Task> act = () => _controller.UpdateSearchParametersStatus(requestBody, TestContext.Current.CancellationToken);
            var exception = await Assert.ThrowsAsync<RequestNotValidException>(act);
            Assert.Equal(Core.Resources.SearchParameterRequestNotValid, exception.Message);
        }

        [Fact]
        public async Task GivenAValidSearchParameterStatusUpdateRequest_WhenSupportsSelectableSearchParametersFlagIsFalse_ThenRequestNotValidExceptionShouldBeReturned()
        {
            CoreFeatureConfiguration coreFeaturesConfiguration = new CoreFeatureConfiguration();
            coreFeaturesConfiguration.SupportsSelectableSearchParameters = false;

            SearchParameterController controller = new SearchParameterController(_mediator, Options.Create(coreFeaturesConfiguration), _fhirConfiguration);
            var requestBody = CreateValidRequestBody();
            Func<System.Threading.Tasks.Task> act = () => controller.UpdateSearchParametersStatus(requestBody, TestContext.Current.CancellationToken);

            var exception = await Assert.ThrowsAsync<RequestNotValidException>(act);
        }

        [Fact]
        public async Task GivenAValidSearchParameterStatusUpdateRequest_WhenServiceIsAzureApiForFhir_ThenRequestNotValidExceptionShouldBeReturned()
        {
            AzureApiForFhirRuntimeConfiguration azureApiForFhirConfiguration = new AzureApiForFhirRuntimeConfiguration();
            SearchParameterController controller = new SearchParameterController(_mediator, Options.Create(_coreFeaturesConfiguration), azureApiForFhirConfiguration);
            var requestBody = CreateValidRequestBody();
            Func<System.Threading.Tasks.Task> act = () => controller.UpdateSearchParametersStatus(requestBody, TestContext.Current.CancellationToken);

            var exception = await Assert.ThrowsAsync<RequestNotValidException>(act);
        }

        [Fact]
        public async Task GivenAValidRequestBody_WhenParsingRequestBody_MediatorShouldBeCalled()
        {
            var requestBody = CreateValidRequestBody();

            try
            {
                await _controller.UpdateSearchParametersStatus(requestBody, TestContext.Current.CancellationToken);
            }
            catch
            {
            }

            await _mediator.Received(1).Send(Arg.Any<SearchParameterStateUpdateRequest>(), TestContext.Current.CancellationToken);
        }

        [Fact]
        public async Task GivenAValidRequestBody_WhenParsingRequestBody_MediatorShouldBeCalledWithCorrectParameters()
        {
            var requestBody = CreateValidRequestBody();
            try
            {
                await _controller.UpdateSearchParametersStatus(requestBody, TestContext.Current.CancellationToken);
            }
            catch
            {
            }

            await _mediator.Received(1).Send(Arg.Is<SearchParameterStateUpdateRequest>(x => x.SearchParameters.Any(sp => sp.Item1 == new Uri(DummyUrl) && sp.Item2 == SearchParameterStatus.Disabled)), TestContext.Current.CancellationToken);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenAnInvalidSearchParameterStatusUpdateRequest_WhenParametersAreEmpty_ThenRequestNotValidExceptionShouldBeThrown(
            bool emptyParameters)
        {
            var fhirRuntimeConfiguration = Substitute.For<IFhirRuntimeConfiguration>();
            fhirRuntimeConfiguration.IsSelectiveSearchParameterSupported.Returns(true);
            _coreFeaturesConfiguration.SupportsSelectableSearchParameters = true;

            var controller = new SearchParameterController(_mediator, Options.Create(_coreFeaturesConfiguration), fhirRuntimeConfiguration);
            var requestBody = emptyParameters ? new Parameters() : null;
            var act = () => controller.UpdateSearchParametersStatus(requestBody, TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<RequestNotValidException>(act);
        }

        private Parameters CreateInvalidRequestBody()
        {
            Parameters parameters = new Parameters();
            List<ParameterComponent> parts = new List<ParameterComponent>
                {
                    new ParameterComponent()
                    {
                        Name = "invalid",
                        Value = new FhirUrl(new Uri(DummyUrl)),
                    },
                    new ParameterComponent()
                    {
                        Name = SearchParameterStateProperties.Status,
                        Value = new FhirString(SearchParameterStatus.Disabled.ToString()),
                    },
                };
            parameters.Parameter.Add(new ParameterComponent()
            {
                Name = SearchParameterStateProperties.Name,
                Part = parts,
            });

            return parameters;
        }

        [Fact]
        public async Task GivenAPostSearchParametersStatusRequest_WhenProcessing_ThenCSearchParameterStateRequestShouldBeCreatedCorrectly()
        {
            var query = "?p0=v0&p1=v1&p2=v2";
            _httpContext.Request.QueryString = new QueryString(query);

            _mediator
                .Send(Arg.Any<SearchParameterStateRequest>())
                .Returns(new SearchParameterStateResponse(new Parameters().ToResourceElement()));

            var request = default(SearchParameterStateRequest);
            _mediator.When(
                x => x.Send(
                    Arg.Any<SearchParameterStateRequest>(),
                    Arg.Any<CancellationToken>()))
                .Do(x => request = x.ArgAt<SearchParameterStateRequest>(0));

            var response = await _controller.PostSearchParametersStatus(CancellationToken.None);
            var result = Assert.IsType<FhirResult>(response);
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.NotNull(result.Result);
            Assert.Equal(KnownResourceTypes.Parameters, result.GetResultTypeName(), StringComparer.OrdinalIgnoreCase);

            Assert.NotNull(request);
            Assert.All(
                _httpContext.Request.Query,
                x =>
                {
                    Assert.Contains(
                        request.Queries,
                        y =>
                        {
                            return string.Equals(x.Key, y.Item1, StringComparison.OrdinalIgnoreCase)
                                && string.Equals(x.Value.ToString(), y.Item2, StringComparison.OrdinalIgnoreCase);
                        });
                });

            await _mediator.Received(1).Send(
                Arg.Any<SearchParameterStateRequest>(),
                Arg.Any<CancellationToken>());
            _mediator.ClearReceivedCalls();
        }

        private Parameters CreateValidRequestBodyWithInvalidStatus()
        {
            Parameters parameters = new Parameters();
            List<ParameterComponent> parts = new List<ParameterComponent>
                {
                    new ParameterComponent()
                    {
                        Name = SearchParameterStateProperties.Url,
                        Value = new FhirUrl(new Uri(DummyUrl)),
                    },
                    new ParameterComponent()
                    {
                        Name = SearchParameterStateProperties.Status,
                        Value = new FhirString(InvalidStatus),
                    },
                };
            parameters.Parameter.Add(new ParameterComponent()
            {
                Name = SearchParameterStateProperties.Name,
                Part = parts,
            });

            return parameters;
        }

        private Parameters CreateValidRequestBody()
        {
            Parameters parameters = new Parameters();
            List<ParameterComponent> parts = new List<ParameterComponent>
                {
                    new ParameterComponent()
                    {
                        Name = SearchParameterStateProperties.Url,
                        Value = new FhirUrl(new Uri(DummyUrl)),
                    },
                    new ParameterComponent()
                    {
                        Name = SearchParameterStateProperties.Status,
                        Value = new FhirString(SearchParameterStatus.Disabled.ToString()),
                    },
                };
            parameters.Parameter.Add(new ParameterComponent()
            {
                Name = SearchParameterStateProperties.Name,
                Part = parts,
            });

            return parameters;
        }
    }
}
