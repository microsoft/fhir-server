// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
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
                // Skip initialization if the request is from a loopback/local IP to avoid using health check requests
                if (!instanceConfiguration.IsInitialized && !IsLoopbackOrLocalRequest(context.Request.Host.Host))
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

        /// <summary>
        /// Determines if the request is from a loopback or local IP address.
        /// This is used to exclude health check requests from initializing the instance configuration.
        /// </summary>
        /// <param name="host">The host name or IP address from the request.</param>
        /// <returns>True if the host is a loopback or local IP address; otherwise, false.</returns>
        private static bool IsLoopbackOrLocalRequest(string host)
        {
            if (string.IsNullOrEmpty(host))
            {
                return false;
            }

            // Remove brackets if present (IPv6 addresses may be wrapped in brackets in Host headers)
            var hostOnly = host.TrimStart('[').TrimEnd(']');

            // Remove port if present. IPv6 addresses have colons in the address itself,
            // so we need to be careful. Only remove port for IPv4:port format (single colon).
            if (hostOnly.Contains(':', StringComparison.Ordinal))
            {
                var colonCount = hostOnly.Count(c => c == ':');
                if (colonCount == 1)
                {
                    // Single colon - likely IPv4:port format, remove port
                    hostOnly = hostOnly.Split(':')[0];
                }

                // If multiple colons, this is IPv6 address - keep as is
            }

            // Check for common loopback/local identifiers
            if (hostOnly.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                hostOnly.Equals("127.0.0.1", StringComparison.Ordinal) ||
                hostOnly.Equals("::1", StringComparison.Ordinal) || // IPv6 loopback
                hostOnly.StartsWith("127.", StringComparison.Ordinal) || // 127.x.x.x range
                hostOnly.StartsWith("192.168.", StringComparison.Ordinal) || // Private network
                hostOnly.StartsWith("10.", StringComparison.Ordinal) || // Private network
                (hostOnly.StartsWith("172.", StringComparison.Ordinal) && IsIn172PrivateRange(hostOnly))) // Private 172.16.0.0/12
            {
                return true;
            }

            return false;
        }

        private static bool IsIn172PrivateRange(string host)
        {
            if (IPAddress.TryParse(host, out var ip))
            {
                var bytes = ip.GetAddressBytes();
                return bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31;
            }

            return false;
        }
    }
}
