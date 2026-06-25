// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using Hl7.Fhir.Model;
using Medino;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Messages.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.UnitTests.Controllers
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class IncludesControllerTests
    {
        private readonly IncludesController _controller;
        private readonly CoreFeatureConfiguration _coreFeatureConfiguration;
        private readonly IMediator _mediator;

        public IncludesControllerTests()
        {
            _mediator = Substitute.For<IMediator>();
            _mediator.SendAsync<SearchResourceResponse>(
                Arg.Any<SearchResourceRequest>(),
                Arg.Any<CancellationToken>())
                .Returns(new SearchResourceResponse(new Bundle().ToResourceElement()));

            _coreFeatureConfiguration = new CoreFeatureConfiguration();
            _controller = new IncludesController(
                _mediator,
                Options.Create(_coreFeatureConfiguration));
            _controller.ControllerContext = new ControllerContext(
                new ActionContext(
                    Substitute.For<HttpContext>(),
                    new RouteData(),
                    new ControllerActionDescriptor()));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenConfiguration_WhenIncludesIsEnabled_ThenSearchResultShouldBeReturned(bool enabled)
        {
            _coreFeatureConfiguration.SupportsIncludes = enabled;
            try
            {
                await _controller.Search(KnownResourceTypes.Patient);
                Assert.True(enabled);
            }
            catch (RequestNotValidException)
            {
                Assert.False(enabled);
            }

            await _mediator.Received(enabled ? 1 : 0).SendAsync<SearchResourceResponse>(
                Arg.Any<SearchResourceRequest>(),
                Arg.Any<CancellationToken>());
        }
    }
}
