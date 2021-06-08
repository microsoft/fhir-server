// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.Features.Context
{
    public class FhirRequestContextMiddleware
    {
        private readonly RequestDelegate _next;

        public FhirRequestContextMiddleware(RequestDelegate next)
        {
            EnsureArg.IsNotNull(next, nameof(next));

            _next = next;
        }

        public async Task Invoke(HttpContext context, RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor, CorrelationIdProvider correlationIdProvider)
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

            // https://www.hl7.org/fhir/http.html#custom
            // If X-Request-Id header is present, then put it value into X-Correlation-Id header for response.
            if (context.Request.Headers.TryGetValue(KnownHeaders.RequestId, out var requestId) && !string.IsNullOrEmpty(requestId))
            {
                context.Response.Headers[KnownHeaders.CorrelationId] = requestId;
            }

            var fhirRequestContext = new FhirRequestContext(
                method: request.Method,
                uriString: uriInString,
                baseUriString: baseUriInString,
                correlationId: correlationId,
                requestHeaders: context.Request.Headers,
                responseHeaders: context.Response.Headers);

            context.Response.Headers[KnownHeaders.RequestId] = correlationId;

            fhirRequestContextAccessor.RequestContext = fhirRequestContext;

            // Call the next delegate/middleware in the pipeline
            await _next(context);
        }
    }
}
