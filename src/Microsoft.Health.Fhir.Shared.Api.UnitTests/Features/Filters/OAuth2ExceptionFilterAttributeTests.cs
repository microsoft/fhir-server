// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Api.Features.Exceptions;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Filters
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.SmartOnFhir)]
    public class OAuth2ExceptionFilterAttributeTests
    {
        private readonly ILogger<OAuth2ExceptionFilterAttribute> _logger;
        private readonly OAuth2ExceptionFilterAttribute _filter;

        public OAuth2ExceptionFilterAttributeTests()
        {
            _logger = Substitute.For<ILogger<OAuth2ExceptionFilterAttribute>>();
            _filter = new OAuth2ExceptionFilterAttribute(_logger);
        }

        [Fact]
        public void GivenOAuth2BadRequestException_WhenOnActionExecuted_ThenReturnsBadRequest()
        {
            var httpContext = new DefaultHttpContext();
            var actionContext = new ActionContext(
                httpContext,
                new RouteData(),
                new ControllerActionDescriptor());

            var context = new ActionExecutedContext(actionContext, new IFilterMetadata[0], new object())
            {
                Exception = new OAuth2BadRequestException("invalid_request", "token parameter is required"),
            };

            _filter.OnActionExecuted(context);

            var result = Assert.IsType<ObjectResult>(context.Result);
            Assert.Equal((int)HttpStatusCode.BadRequest, result.StatusCode);
            Assert.True(context.ExceptionHandled);
        }

        [Fact]
        public void GivenUnknownException_WhenOnActionExecuted_ThenReturnsInternalServerError()
        {
            var httpContext = new DefaultHttpContext();
            var actionContext = new ActionContext(
                httpContext,
                new RouteData(),
                new ControllerActionDescriptor());

            var context = new ActionExecutedContext(actionContext, new IFilterMetadata[0], new object())
            {
                Exception = new System.Exception("unknown"),
            };

            _filter.OnActionExecuted(context);

            var result = Assert.IsType<ObjectResult>(context.Result);
            Assert.Equal((int)HttpStatusCode.InternalServerError, result.StatusCode);
            Assert.True(context.ExceptionHandled);
        }

        [Fact]
        public void GivenNullException_WhenOnActionExecuted_ThenNoActionShouldBeExecuted()
        {
            var httpContext = new DefaultHttpContext();
            var actionContext = new ActionContext(
                httpContext,
                new RouteData(),
                new ControllerActionDescriptor());

            var context = new ActionExecutedContext(actionContext, new IFilterMetadata[0], new object())
            {
                Exception = null,
            };

            _filter.OnActionExecuted(context);

            Assert.Null(context.Result);
            Assert.False(context.ExceptionHandled);
        }
    }
}
