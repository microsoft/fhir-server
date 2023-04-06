// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations.SearchParameterState;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Messages.SearchParameterState;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using static Hl7.Fhir.Model.Parameters;

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

        [Fact]
        public async void GivenAnInvalidRequestBody_WhenParsingRequestBody_RequestNotValidExceptionIsThrown()
        {
            var requestBody = CreateInvalidRequestBody();

            Func<System.Threading.Tasks.Task> act = () => _controller.UpdateSearchParametersStatus(requestBody, default(CancellationToken));
            var exception = await Assert.ThrowsAsync<RequestNotValidException>(act);
            Assert.Equal(Core.Resources.SearchParameterRequestNotValid, exception.Message);
        }

        [Fact]
        public async void GivenAnInvalidStatusInRequest_WhenParsingRequestBody_RequestNotValidExceptionIsThrown()
        {
            Parameters requestBody = new Parameters();
            string invalidStatus = "inValideStatus";
            string url = "http://someresouce";
            List<ParameterComponent> parts = new List<ParameterComponent>
                {
                    new ParameterComponent()
                    {
                        Name = SearchParameterStateProperties.Url,
                        Value = new FhirUrl(new Uri(url)),
                    },
                    new ParameterComponent()
                    {
                        Name = SearchParameterStateProperties.Status,
                        Value = new FhirString(invalidStatus),
                    },
                };
            requestBody.Parameter.Add(new Parameters.ParameterComponent()
            {
                Name = SearchParameterStateProperties.Name,
                Part = parts,
            });

            Func<System.Threading.Tasks.Task> act = () => _controller.UpdateSearchParametersStatus(requestBody, default(CancellationToken));
            var exception = await Assert.ThrowsAsync<RequestNotValidException>(act);
            Assert.Equal(string.Format(Core.Resources.SearchParameterStatusNotValid, invalidStatus, url), exception.Message);
        }

        private Parameters CreateInvalidRequestBody()
        {
            Parameters parameters = new Parameters();
            List<ParameterComponent> parts = new List<ParameterComponent>
                {
                    new ParameterComponent()
                    {
                        Name = "invalid",
                        Value = new FhirUrl(new Uri("http://someresouce")),
                    },
                    new ParameterComponent()
                    {
                        Name = SearchParameterStateProperties.Status,
                        Value = new FhirString(SearchParameterStatus.Disabled.ToString()),
                    },
                };
            parameters.Parameter.Add(new Parameters.ParameterComponent()
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
                        Value = new FhirUrl(new Uri("http://someresouce")),
                    },
                    new ParameterComponent()
                    {
                        Name = SearchParameterStateProperties.Status,
                        Value = new FhirString(SearchParameterStatus.Disabled.ToString()),
                    },
                };
            parameters.Parameter.Add(new Parameters.ParameterComponent()
            {
                Name = SearchParameterStateProperties.Name,
                Part = parts,
            });

            return parameters;
        }
    }
}
