// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Health.Fhir.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Security;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Audit
{
    public class AuditMiddlewareTests
    {
        private const string Controller = "Fhir";
        private const string Action = "Action";

        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        private readonly IClaimsExtractor _claimsExtractor = Substitute.For<IClaimsExtractor>();
        private readonly IAuditHelper _auditHelper = Substitute.For<IAuditHelper>();

        private readonly AuditMiddleware _auditMiddleware;

        private readonly HttpContext _httpContext = new DefaultHttpContext();
        private readonly IFhirRequestContext _fhirRequestContext = Substitute.For<IFhirRequestContext>();

        public AuditMiddlewareTests()
        {
            _fhirRequestContextAccessor.FhirRequestContext.Returns(_fhirRequestContext);

            _auditMiddleware = new AuditMiddleware(
                httpContext => Task.CompletedTask,
                _fhirRequestContextAccessor,
                _claimsExtractor,
                _auditHelper);
        }

        [Fact]
        public async Task GivenRouteNameSet_WhenInvoked_ThenAuditLogShouldNotBeLogged()
        {
            _fhirRequestContext.RouteName.Returns("route");

            await _auditMiddleware.Invoke(_httpContext);

            _auditHelper.DidNotReceiveWithAnyArgs().LogExecuted(
                controllerName: default,
                actionName: default,
                responseResultType: default,
                httpContext: default,
                claimsExtractor: default);
        }

        [Fact]
        public async Task GivenRouteNameNotSetAndNotAuthXFailure_WhenInvoked_ThenAuditLogShouldNotBeLogged()
        {
            _httpContext.Response.StatusCode = (int)HttpStatusCode.OK;

            await _auditMiddleware.Invoke(_httpContext);

            _auditHelper.DidNotReceiveWithAnyArgs().LogExecuted(
                controllerName: default,
                actionName: default,
                responseResultType: default,
                httpContext: default,
                claimsExtractor: default);
        }

        [Theory]
        [InlineData(HttpStatusCode.Unauthorized)]
        [InlineData(HttpStatusCode.Forbidden)]
        public async Task GivenRouteNameNotSetAndAuthXFailed_WhenInvoked_ThenAuditLogShouldBeLogged(HttpStatusCode statusCode)
        {
            const string resourceType = "Patient";

            _httpContext.Response.StatusCode = (int)statusCode;

            RouteData routeData = SetupRouteData();

            routeData.Values.Add(KnownActionParameterNames.ResourceType, resourceType);

            await _auditMiddleware.Invoke(_httpContext);

            _auditHelper.Received(1).LogExecuted(Controller, Action, resourceType, _httpContext, _claimsExtractor);
        }

        [Fact]
        public async Task GivenRouteNameNotSetAuthXFailedAndControllerActionNotSet_WhenInvoked_ThenAuditLogShouldBeLogged()
        {
            const HttpStatusCode statusCode = HttpStatusCode.Forbidden;

            _httpContext.Response.StatusCode = (int)statusCode;

            RouteData routeData = SetupRouteData(controllerName: null, actionName: null);

            await _auditMiddleware.Invoke(_httpContext);

            _auditHelper.Received(1).LogExecuted(null, null, null, _httpContext, _claimsExtractor);
        }

        [Fact]
        public async Task GivenRouteNameNotSetAuthXFailedAndResourceTypeNotSet_WhenInvoked_ThenAuditLogShouldBeLogged()
        {
            const HttpStatusCode statusCode = HttpStatusCode.Forbidden;

            _httpContext.Response.StatusCode = (int)statusCode;

            RouteData routeData = SetupRouteData();

            await _auditMiddleware.Invoke(_httpContext);

            _auditHelper.Received(1).LogExecuted(Controller, Action, null, _httpContext, _claimsExtractor);
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
