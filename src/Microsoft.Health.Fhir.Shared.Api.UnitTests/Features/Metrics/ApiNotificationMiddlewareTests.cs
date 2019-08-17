// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Health.Fhir.Api.Features.Metrics;
using Microsoft.Health.Fhir.Core.Features.Context;
using NSubstitute;

namespace Microsoft.Health.Fhir.Shared.Api.UnitTests.Features.Metrics
{
    public class ApiNotificationMiddlewareTests
    {
        private const string AuthenticationType = "AuthenticationTypes.Federation";
        private const string Scheme = "https";
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        private readonly ClaimsPrincipal _principal = Substitute.ForPartsOf<ClaimsPrincipal>();

        private readonly ApiNotificationMiddleware _metricMiddleware;

        private readonly HttpContext _httpContext;
        private readonly IFhirRequestContext _fhirRequestContext = Substitute.For<IFhirRequestContext>();

        public ApiNotificationMiddlewareTests()
        {
            _httpContext = new DefaultHttpContext();
            _httpContext.Request.Scheme = Scheme;

            _fhirRequestContextAccessor.FhirRequestContext.Returns(_fhirRequestContext);

            _metricMiddleware = new ApiNotificationMiddleware(
                httpContext => Task.CompletedTask,
                _fhirRequestContextAccessor,
                Substitute.For<IMediator>());

            var identity = Substitute.For<IIdentity>();
            identity.AuthenticationType.Returns(AuthenticationType);
            _principal.Identity.Returns(identity);
            _fhirRequestContext.Principal = _principal;
            RouteDataHelpers.SetupRouteData(_fhirRequestContext, _httpContext, "Fhir", "Action");
        }
    }
}
