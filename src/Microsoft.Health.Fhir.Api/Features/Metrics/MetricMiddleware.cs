// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Metrics;

namespace Microsoft.Health.Fhir.Api.Features.Metrics
{
    public class MetricMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;
        private readonly IMetricLogger _latencyMetricLogger;
        private readonly IMetricLogger _totalRequestsLogger;
        private readonly IMetricLogger _errorRequestsLogger;

        public MetricMiddleware(
            RequestDelegate next,
            IFhirRequestContextAccessor fhirRequestContextAccessor,
            IMetricLoggerFactory metricLoggerFactory)
        {
            EnsureArg.IsNotNull(next, nameof(next));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(metricLoggerFactory, nameof(metricLoggerFactory));

            _next = next;
            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _latencyMetricLogger = metricLoggerFactory.CreateMetricLogger("Latency", "Operation", "Authentication", "Protocol", "ResourceType");
            _totalRequestsLogger = metricLoggerFactory.CreateMetricLogger("Requests", "Operation", "Authentication", "Protocol", "ResourceType", "StatusCode", "StatusCodeClass", "StatusText");
            _errorRequestsLogger = metricLoggerFactory.CreateMetricLogger("Errors", "Operation", "Authentication", "Protocol", "ResourceType", "StatusCode", "StatusCodeClass", "StatusText");
        }

        public async Task Invoke(HttpContext context)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                await _next(context);
            }
            finally
            {
                stopwatch.Stop();

                RouteData routeData = context.GetRouteData();
                routeData.Values.TryGetValue(KnownActionParameterNames.ResourceType, out object resourceType);

                _latencyMetricLogger.LogMetric(
                                            stopwatch.ElapsedMilliseconds,
                                            _fhirRequestContextAccessor.FhirRequestContext.RouteName,
                                            _fhirRequestContextAccessor.FhirRequestContext.Principal.Identity.AuthenticationType,
                                            context.Request.Protocol,
                                            resourceType?.ToString());

                _totalRequestsLogger.LogMetric(
                    1,
                    _fhirRequestContextAccessor.FhirRequestContext.RouteName,
                    _fhirRequestContextAccessor.FhirRequestContext.Principal.Identity.AuthenticationType,
                    context.Request.Protocol,
                    resourceType?.ToString(),
                    context.Response.StatusCode.ToString(CultureInfo.InvariantCulture),
                    (context.Response.StatusCode / 100) + "xx",
                    ((HttpStatusCode)context.Response.StatusCode).ToString());

                if (context.Response.StatusCode >= 500)
                {
                    _errorRequestsLogger.LogMetric(
                        1,
                        _fhirRequestContextAccessor.FhirRequestContext.RouteName,
                        _fhirRequestContextAccessor.FhirRequestContext.Principal.Identity.AuthenticationType,
                        context.Request.Protocol,
                        resourceType?.ToString(),
                        context.Response.StatusCode.ToString(CultureInfo.InvariantCulture),
                        (context.Response.StatusCode / 100) + "xx",
                        ((HttpStatusCode)context.Response.StatusCode).ToString());
                }
            }
        }
    }
}
