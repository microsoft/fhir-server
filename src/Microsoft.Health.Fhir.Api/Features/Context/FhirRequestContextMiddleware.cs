// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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

        public async Task Invoke(
            HttpContext context,
            RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor,
            IFhirServerInstanceConfiguration instanceConfiguration,
            CorrelationIdProvider correlationIdProvider)
        {
            HttpRequest request = context.Request;

            string baseUriInString = UriHelper.BuildAbsolute(
                request.Scheme,
                request.Host,
                request.PathBase,
                "/");

            string uriInString = UriHelper.BuildAbsolute(
                request.Scheme,
                request.Host,
                request.PathBase,
                request.Path,
                request.QueryString);

            string correlationId = correlationIdProvider.Invoke();
            string vanityUrlString = null;

            try
            {
                // Check if X-MS-VANITY-URL header is present in the request
                if (context.Request.Headers.TryGetValue(KnownHeaders.VanityUrl, out var vanityUrlHeader) && !string.IsNullOrEmpty(vanityUrlHeader))
                {
                    vanityUrlString = vanityUrlHeader.ToString();
                }

                // Initialize the global instance configuration on first request (thread-safe, idempotent)
                // This ensures background services have access to base URI and vanity URL even when there's no active HTTP context
                // Note this is set only once per application lifetime. If vanity URL changes, a restart is required to pick up the new value.
                if (!instanceConfiguration.IsInitialized)
                {
                    instanceConfiguration.Initialize(baseUriInString, vanityUrlString);
                }
            }
            catch (Exception)
            {
                // Carry on. Any jobs depending on instance configuration will fail later if initialization was unsuccessful.
            }

            // Set vanity URL in response headers (from request header or default to base URI)
            if (!string.IsNullOrEmpty(vanityUrlString))
            {
                context.Response.Headers[KnownHeaders.VanityUrl] = vanityUrlString;
            }

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
            context.Response.Headers[XContentTypeOptions] = XContentTypeOptionsValue;
            context.Response.Headers[XFrameOptions] = XFrameOptionsValue;
            context.Response.Headers[ContentSecurityPolicy] = ContentSecurityPolicyValue;

            fhirRequestContextAccessor.RequestContext = fhirRequestContext;

            // Call the next delegate/middleware in the pipeline
            await _next(context);
        }
    }
}
