// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Security.Claims;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Routing;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Controllers
{
    public class FhirControllerTests
    {
        private readonly IMediator _mediator = Substitute.For<IMediator>();
        private readonly ILogger<FhirController> _logger = NullLogger<FhirController>.Instance;
        private readonly IFhirRequestContextAccessor _contextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        private readonly IUrlResolver _urlResolver = Substitute.For<IUrlResolver>();
        private readonly FeatureConfiguration _featureConfiguration = new FeatureConfiguration();
        private readonly IAuthorizationService _authorizationService = Substitute.For<IAuthorizationService>();
        private readonly FhirController _controller;

        public FhirControllerTests()
        {
            _controller = new FhirController(
                _mediator,
                _logger,
                _contextAccessor,
                _urlResolver,
                Options.Create(_featureConfiguration),
                _authorizationService);
        }

        [Fact]
        public async Task GivenHardDeleteActionNotAuthorized_WhenRequestingHardDeleteAction_ThenForbiddenResultShouldBeReturned()
        {
            _authorizationService.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<object>(), policyName: "HardDelete").Returns(Task.FromResult(AuthorizationResult.Failed()));
            var result = await _controller.Delete("typea", "ida", true);
            Assert.IsType<ForbidResult>(result);
        }
    }
}
