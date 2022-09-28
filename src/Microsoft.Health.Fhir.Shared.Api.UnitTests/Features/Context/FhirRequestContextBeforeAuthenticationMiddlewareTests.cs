// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.UnitTests.Features.Context;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Context
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public class FhirRequestContextBeforeAuthenticationMiddlewareTests
    {
        private const string ControllerName = "controller";
        private const string ActionName = "action";
        private const string DefaultAuditEventType = "audit";

        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
        private readonly IAuditEventTypeMapping _auditEventTypeMapping = Substitute.For<IAuditEventTypeMapping>();

        private readonly FhirRequestContextBeforeAuthenticationMiddleware _fhirRequestContextBeforeAuthenticationMiddleware;

        private readonly DefaultFhirRequestContext _fhirRequestContext = new DefaultFhirRequestContext();
        private readonly HttpContext _httpContext = new DefaultHttpContext();

        public FhirRequestContextBeforeAuthenticationMiddlewareTests()
        {
            _fhirRequestContextAccessor.RequestContext.Returns(_fhirRequestContext);

            _fhirRequestContextBeforeAuthenticationMiddleware = new FhirRequestContextBeforeAuthenticationMiddleware(
                httpContext => Task.CompletedTask,
                _fhirRequestContextAccessor,
                _auditEventTypeMapping);
        }

        [Fact]
        public async Task GivenRouteNameSet_WhenInvoked_ThenAuditLogShouldNotBeLogged()
        {
            _fhirRequestContext.RouteName = "route";

            await _fhirRequestContextBeforeAuthenticationMiddleware.Invoke(_httpContext);

            Assert.Null(_fhirRequestContext.AuditEventType);
            Assert.Null(_fhirRequestContext.ResourceType);
        }

        [Fact]
        public async Task GivenRouteNameNotSetAndNotAuthXFailure_WhenInvoked_ThenAuditLogShouldNotBeLogged()
        {
            _httpContext.Response.StatusCode = (int)HttpStatusCode.OK;

            await _fhirRequestContextBeforeAuthenticationMiddleware.Invoke(_httpContext);

            Assert.Null(_fhirRequestContext.AuditEventType);
            Assert.Null(_fhirRequestContext.ResourceType);
        }

        [Theory]
        [InlineData(HttpStatusCode.Unauthorized)]
        [InlineData(HttpStatusCode.Forbidden)]
        public async Task GivenRouteNameNotSetAndAuthXFailed_WhenInvoked_ThenAuditLogShouldBeLogged(HttpStatusCode statusCode)
        {
            const string resourceType = "Patient";

            _auditEventTypeMapping.GetAuditEventType(ControllerName, ActionName).Returns(DefaultAuditEventType);

            _httpContext.Response.StatusCode = (int)statusCode;

            RouteData routeData = SetupRouteData(ControllerName, ActionName);

            routeData.Values.Add("typeParameter", resourceType);

            await _fhirRequestContextBeforeAuthenticationMiddleware.Invoke(_httpContext);

            Assert.Equal(DefaultAuditEventType, _fhirRequestContext.AuditEventType);
            Assert.Equal(resourceType, _fhirRequestContext.ResourceType);
        }

        [Fact]
        public async Task GivenRouteNameNotSetAuthXFailedAndControllerActionNotSet_WhenInvoked_ThenAuditLogShouldBeLogged()
        {
            _auditEventTypeMapping.GetAuditEventType(default, default).ReturnsForAnyArgs((string)null);

            const HttpStatusCode statusCode = HttpStatusCode.Forbidden;

            _httpContext.Response.StatusCode = (int)statusCode;

            SetupRouteData(controllerName: null, actionName: null);

            await _fhirRequestContextBeforeAuthenticationMiddleware.Invoke(_httpContext);

            Assert.Null(_fhirRequestContext.AuditEventType);
            Assert.Null(_fhirRequestContext.ResourceType);
        }

        [Fact]
        public async Task GivenRouteNameNotSetAuthXFailedAndResourceTypeNotSet_WhenInvoked_ThenAuditLogShouldBeLogged()
        {
            const HttpStatusCode statusCode = HttpStatusCode.Forbidden;

            _auditEventTypeMapping.GetAuditEventType(ControllerName, ActionName).Returns(DefaultAuditEventType);

            _httpContext.Response.StatusCode = (int)statusCode;

            SetupRouteData(ControllerName, ActionName);

            await _fhirRequestContextBeforeAuthenticationMiddleware.Invoke(_httpContext);

            Assert.Equal(DefaultAuditEventType, _fhirRequestContext.AuditEventType);
            Assert.Null(_fhirRequestContext.ResourceType);
        }

        private RouteData SetupRouteData(string controllerName = ControllerName, string actionName = ActionName)
        {
            _fhirRequestContext.RouteName = null;

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
