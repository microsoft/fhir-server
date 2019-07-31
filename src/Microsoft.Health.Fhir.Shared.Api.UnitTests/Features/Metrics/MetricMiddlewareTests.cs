// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Health.Fhir.Api.Features.Metrics;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Metrics;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Api.UnitTests.Features.Metrics
{
    public class MetricMiddlewareTests
    {
        private const string AuthenticationType = "AuthenticationTypes.Federation";
        private const string Scheme = "https";
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        private readonly IMetricLoggerFactory _metricLoggerFactory = Substitute.For<IMetricLoggerFactory>();
        private readonly IMetricLogger _latencyMetric = Substitute.For<IMetricLogger>();
        private readonly IMetricLogger _requestMetric = Substitute.For<IMetricLogger>();
        private readonly IMetricLogger _errorMetric = Substitute.For<IMetricLogger>();
        private readonly ClaimsPrincipal _principal = Substitute.ForPartsOf<ClaimsPrincipal>();

        private readonly MetricMiddleware _metricMiddleware;

        private readonly HttpContext _httpContext;
        private readonly IFhirRequestContext _fhirRequestContext = Substitute.For<IFhirRequestContext>();

        public MetricMiddlewareTests()
        {
            _httpContext = new DefaultHttpContext();
            _httpContext.Request.Scheme = Scheme;

            _fhirRequestContextAccessor.FhirRequestContext.Returns(_fhirRequestContext);

            _latencyMetric.MetricName = "Latency";
            _requestMetric.MetricName = "Requests";
            _errorMetric.MetricName = "Errors";

            _metricLoggerFactory.CreateMetricLogger(Arg.Is<string>("Latency"), Arg.Any<string[]>()).Returns(_latencyMetric);
            _metricLoggerFactory.CreateMetricLogger(Arg.Is<string>("Requests"), Arg.Any<string[]>()).Returns(_requestMetric);
            _metricLoggerFactory.CreateMetricLogger(Arg.Is<string>("Errors"), Arg.Any<string[]>()).Returns(_errorMetric);

            _metricMiddleware = new MetricMiddleware(
                httpContext => Task.CompletedTask,
                _fhirRequestContextAccessor,
                _metricLoggerFactory);

            var identity = Substitute.For<IIdentity>();
            identity.AuthenticationType.Returns(AuthenticationType);
            _principal.Identity.Returns(identity);
            _fhirRequestContext.Principal = _principal;
            RouteDataHelpers.SetupRouteData(_fhirRequestContext, _httpContext, "Fhir", "Action");
        }

        [Theory]
        [InlineData(HttpStatusCode.OK, "OK", "2xx")]
        [InlineData(HttpStatusCode.Accepted, "Accepted", "2xx")]
        [InlineData(HttpStatusCode.Found, "Found", "3xx")]
        [InlineData(HttpStatusCode.BadRequest, "BadRequest", "4xx")]
        public async Task SuccessfulRequest_Logs_Latency_Requests_NotErrors(HttpStatusCode statusCode, string stringStatusCode, string statusCodeClass)
        {
            _fhirRequestContext.RouteName.Returns("SearchAllResources");
            _httpContext.Response.StatusCode = (int)statusCode;

            await _metricMiddleware.Invoke(_httpContext);

            _latencyMetric.ReceivedWithAnyArgs(1).LogMetric(Arg.Any<long>(), _fhirRequestContext.RouteName, AuthenticationType, Scheme, null);
            _requestMetric.ReceivedWithAnyArgs(1).LogMetric(1, _fhirRequestContext.RouteName, AuthenticationType, Scheme, null, ((int)statusCode).ToString(CultureInfo.InvariantCulture), stringStatusCode, statusCodeClass);
        }

        [Theory]
        [InlineData(HttpStatusCode.InternalServerError, "InternalServerError", "5xx")]
        [InlineData(HttpStatusCode.BadGateway, "BadGateway", "5xx")]
        public async Task FailedRequest_Logs_Latency_Requests_Errors(HttpStatusCode statusCode, string stringStatusCode, string statusCodeClass)
        {
            _fhirRequestContext.RouteName.Returns("SearchAllResources");
            _httpContext.Response.StatusCode = (int)statusCode;

            await _metricMiddleware.Invoke(_httpContext);

            _latencyMetric.ReceivedWithAnyArgs(1).LogMetric(Arg.Any<long>(), _fhirRequestContext.RouteName, AuthenticationType, "Scheme", null);
            _requestMetric.ReceivedWithAnyArgs(1).LogMetric(1, _fhirRequestContext.RouteName, AuthenticationType, "Scheme", null, ((int)statusCode).ToString(CultureInfo.InvariantCulture), stringStatusCode, statusCodeClass);
            _errorMetric.ReceivedWithAnyArgs(1).LogMetric(1, _fhirRequestContext.RouteName, AuthenticationType, "Scheme", null, ((int)statusCode).ToString(CultureInfo.InvariantCulture), stringStatusCode, statusCodeClass);
        }
    }
}
