﻿// -------------------------------------------------------------------------------------------------
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
        internal const string XContentTypeOptions = "X-Content-Type-Options";
        private const string XContentTypeOptionsValue = "nosniff";

        internal const string XFrameOptions = "X-Frame-Options";
        private const string XFrameOptionsValue = "SAMEORIGIN";

        internal const string ContentSecurityPolicy = "Content-Security-Policy";
        private const string ContentSecurityPolicyValue = "frame-src 'self';";

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

            var fhirRequestContext = new FhirRequestContext(
                method: request.Method,
                uriString: uriInString,
                baseUriString: baseUriInString,
                correlationId: correlationId,
                requestHeaders: context.Request.Headers,
                responseHeaders: context.Response.Headers);

            // https://www.hl7.org/fhir/http.html#custom
            // If X-Request-Id header is present, then put it value into X-Correlation-Id header for response.
            if (context.Request.Headers.TryGetValue(KnownHeaders.RequestId, out var requestId) && !string.IsNullOrEmpty(requestId))
            {
                fhirRequestContext.ResponseHeaders[KnownHeaders.CorrelationId] = requestId;
            }

            fhirRequestContext.ResponseHeaders[KnownHeaders.RequestId] = correlationId;

            fhirRequestContext.ResponseHeaders[XContentTypeOptions] = XContentTypeOptionsValue;
            fhirRequestContext.ResponseHeaders[XFrameOptions] = XFrameOptionsValue;
            fhirRequestContext.ResponseHeaders[ContentSecurityPolicy] = ContentSecurityPolicyValue;

            fhirRequestContextAccessor.RequestContext = fhirRequestContext;

            // Call the next delegate/middleware in the pipeline
            await _next(context);
        }
    }
}
