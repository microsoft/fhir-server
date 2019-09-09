// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Context;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.Features.Context
{
    public class FhirRequestContextMiddleware
    {
        private const string RequestIdHeaderName = "X-Request-Id";

        private readonly RequestDelegate _next;

        public FhirRequestContextMiddleware(RequestDelegate next)
        {
            EnsureArg.IsNotNull(next, nameof(next));

            _next = next;
        }

        public Task Invoke(HttpContext context, IFhirRequestContextAccessor fhirRequestContextAccessor, CorrelationIdProvider correlationIdProvider)
        {
            HttpRequest request = context.Request;

            string baseUriInString = UriHelper.BuildAbsolute(
                request.Scheme,
                request.Host,
                request.PathBase);

            string uriInString = UriHelper.BuildAbsolute(
                request.Scheme,
                request.Host,
                request.PathBase,
                request.Path,
                request.QueryString);

            string correlationId = correlationIdProvider.Invoke();

            object resourceType = null;

            RouteData routeData = context.GetRouteData();
            if (routeData != null && routeData.Values != null)
            {
                routeData.Values.TryGetValue(KnownActionParameterNames.ResourceType, out resourceType);
            }

            var fhirRequestContext = new FhirRequestContext(
                method: request.Method,
                uriString: uriInString,
                baseUriString: baseUriInString,
                correlationId: correlationId,
                requestHeaders: context.Request.Headers,
                responseHeaders: context.Response.Headers,
                resourceType: resourceType?.ToString());

            context.Response.Headers[RequestIdHeaderName] = correlationId;

            // Note that if this is executed before authentication occurs, the user will not contain any claims.
            if (context.User != null)
            {
                fhirRequestContext.Principal = context.User;
            }

            fhirRequestContextAccessor.FhirRequestContext = fhirRequestContext;

            // Call the next delegate/middleware in the pipeline
            return _next(context);
        }
    }
}
