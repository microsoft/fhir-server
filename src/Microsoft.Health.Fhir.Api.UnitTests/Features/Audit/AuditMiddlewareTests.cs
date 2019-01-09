// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Health.Fhir.Api.Features.Audit;
using Microsoft.Health.Fhir.Core.Features.Context;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Audit
{
    public class AuditMiddlewareTests
    {
        private const string Controller = "Fhir";
        private const string Action = "Action";

        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        private readonly IAuditHelper _auditHelper = Substitute.For<IAuditHelper>();

        private readonly AuditMiddleware _auditMiddleware;

        private readonly HttpContext _httpContext;
        private readonly IFhirRequestContext _fhirRequestContext = Substitute.For<IFhirRequestContext>();

        public AuditMiddlewareTests()
        {
            _httpContext = new DefaultHttpContext();

            _fhirRequestContextAccessor.FhirRequestContext.Returns(_fhirRequestContext);

            _auditMiddleware = new AuditMiddleware(
                httpContext => Task.CompletedTask,
                _fhirRequestContextAccessor,
                _auditHelper);
        }

        [Fact]
        public async Task GivenRouteNameSet_WhenInvoked_ThenAuditLogShouldNotBeLogged()
        {
            _fhirRequestContext.RouteName.Returns("route");

            await _auditMiddleware.Invoke(_httpContext);

            _auditHelper.DidNotReceiveWithAnyArgs().LogExecuted(null, null, HttpStatusCode.OK, null);
        }

        [Fact]
        public async Task GivenRouteNameNotSetAndNotAuthXFailure_WhenInvoked_ThenAuditLogShouldNotBeLogged()
        {
            _httpContext.Response.StatusCode = (int)HttpStatusCode.OK;

            await _auditMiddleware.Invoke(_httpContext);

            _auditHelper.DidNotReceiveWithAnyArgs().LogExecuted(null, null, HttpStatusCode.OK, null);
        }

        [Theory]
        [InlineData(HttpStatusCode.Unauthorized)]
        [InlineData(HttpStatusCode.Forbidden)]
        public async Task GivenRouteNameNotSetAndAuthXFailed_WhenInvoked_ThenAuditLogShouldBeLogged(HttpStatusCode statusCode)
        {
            const string resourceType = "Patient";

            _httpContext.Response.StatusCode = (int)statusCode;

            RouteData routeData = SetupRouteData();

            routeData.Values.Add("type", resourceType);

            await _auditMiddleware.Invoke(_httpContext);

            _auditHelper.Received(1).LogExecuted(Controller, Action, statusCode, resourceType);
        }

        [Fact]
        public async Task GivenRouteNameNotSetAuthXFailedAndControllerActionNotSet_WhenInvoked_ThenAuditLogShouldBeLogged()
        {
            const HttpStatusCode statusCode = HttpStatusCode.Forbidden;

            _httpContext.Response.StatusCode = (int)statusCode;

            RouteData routeData = SetupRouteData(controllerName: null, actionName: null);

            await _auditMiddleware.Invoke(_httpContext);

            _auditHelper.Received(1).LogExecuted(null, null, statusCode, null);
        }

        [Fact]
        public async Task GivenRouteNameNotSetAuthXFailedAndResourceTypeNotSet_WhenInvoked_ThenAuditLogShouldBeLogged()
        {
            const HttpStatusCode statusCode = HttpStatusCode.Forbidden;

            _httpContext.Response.StatusCode = (int)statusCode;

            RouteData routeData = SetupRouteData();

            await _auditMiddleware.Invoke(_httpContext);

            _auditHelper.Received(1).LogExecuted(Controller, Action, statusCode, null);
        }

        private RouteData SetupRouteData(string controllerName = Controller, string actionName = Action)
        {
            _fhirRequestContext.RouteName.Returns((string)null);

            var routeData = new RouteData();

            routeData.Values.Add("controller", controllerName);
            routeData.Values.Add("action", actionName);

            IRoutingFeature routingFeature = Substitute.For<IRoutingFeature>();

            routingFeature.RouteData.Returns(routeData);

            _httpContext.Features[typeof(IRoutingFeature)] = routingFeature;

            return routeData;
        }
    }
}
