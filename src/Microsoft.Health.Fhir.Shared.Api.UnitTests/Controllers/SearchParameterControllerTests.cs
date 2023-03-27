// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using MediatR;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Controllers
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SearchParameterControllerTests
    {
        private readonly CoreFeatureConfiguration _coreFeaturesConfiguration = new CoreFeatureConfiguration();
        private readonly IMediator _mediator = Substitute.For<IMediator>();
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
        private readonly SearchParameterController _controller;

        public SearchParameterControllerTests()
        {
            _controller = new SearchParameterController(
                _mediator,
                _searchParameterDefinitionManager,
                Options.Create(_coreFeaturesConfiguration));
        }

        [Fact]
        public async void GivenASearchParameterStatusRequest_WhenSupportsSelectiveSearchParametersFlagIsFalse_ThenRequestNotValidExceptionShouldBeReturned()
        {
            CoreFeatureConfiguration coreFeaturesConfiguration = new CoreFeatureConfiguration();
            coreFeaturesConfiguration.SupportsSelectiveSearchParameters = false;

            SearchParameterController controller = new SearchParameterController(_mediator, _searchParameterDefinitionManager, Options.Create(coreFeaturesConfiguration));

            Func<System.Threading.Tasks.Task> act = () => controller.GetSearchParameters(default(CancellationToken));

            var exception = await Assert.ThrowsAsync<RequestNotValidException>(act);
        }
    }
}
