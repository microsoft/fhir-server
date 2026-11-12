// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Partitions;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.Features.Context
{
    public class FhirRequestContextMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<FhirRequestContextMiddleware> _logger;
        private readonly IPartitionManager _partitionManager;

        internal const string XContentTypeOptions = "X-Content-Type-Options";
        private const string XContentTypeOptionsValue = "nosniff";

        internal const string XFrameOptions = "X-Frame-Options";
        private const string XFrameOptionsValue = "SAMEORIGIN";

        internal const string ContentSecurityPolicy = "Content-Security-Policy";
        private const string ContentSecurityPolicyValue = "frame-src 'self';";

        internal static readonly object OriginalPathKey = new object();

        public FhirRequestContextMiddleware(
            RequestDelegate next,
            ILogger<FhirRequestContextMiddleware> logger,
            IPartitionManager partitionManager)
        {
            EnsureArg.IsNotNull(next, nameof(next));
            EnsureArg.IsNotNull(partitionManager, nameof(partitionManager));

            _next = next;
            _logger = logger;
            _partitionManager = partitionManager;
        }

        public async Task Invoke(
            HttpContext context,
            RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor,
            IFhirServerInstanceConfiguration instanceConfiguration,
            CorrelationIdProvider correlationIdProvider)
        {
            HttpRequest request = context.Request;
            PathString originalPath = request.Path;
            PathString originalPathBase = request.PathBase;
            context.Items[OriginalPathKey] = originalPath;

            if (_partitionManager.Partitions.Count > 0
                && request.Path.StartsWithSegments(new PathString("/partition"), StringComparison.OrdinalIgnoreCase, out PathString keyword, out PathString afterKeyword))
            {
                string partitionId = afterKeyword.Value?.Split('/', 3).ElementAtOrDefault(1);
                if (!string.IsNullOrEmpty(partitionId))
                {
                    // Normalize all candidate IDs identically; validation must wait until after authentication.
                    afterKeyword.StartsWithSegments(new PathString("/" + partitionId), StringComparison.Ordinal, out PathString idPath, out PathString remaining);
                    request.PathBase = request.PathBase.Add(keyword).Add(idPath);
                    request.Path = remaining;
                }
            }

            try
            {
                await InvokeRequestAsync(context, fhirRequestContextAccessor, instanceConfiguration, correlationIdProvider, originalPathBase);
            }
            finally
            {
                request.Path = originalPath;
                request.PathBase = originalPathBase;
            }
        }

        private async Task InvokeRequestAsync(
            HttpContext context,
            RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor,
            IFhirServerInstanceConfiguration instanceConfiguration,
            CorrelationIdProvider correlationIdProvider,
            PathString instancePathBase)
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

            // The correlation ID is returned as the request ID for the response. This value can be provided by the client to help link a request to internal logs.
            _logger.LogInformation("Request ID for the response is {CorrelationId}", correlationId);

            try
            {
                // Initialize the global instance configuration on first request (thread-safe, idempotent)
                // This ensures background services have access to base URI even when there's no active HTTP context
                // Note this is set only once per application lifetime.
                // Skip initialization if the request is from a loopback/local IP to avoid using health check requests
                if (!FhirRequestContextMiddlewareExtensions.IsLoopbackOrLocalRequest(context.Request.Host.Host))
                {
                    // Initialize baseUri independently - this will only succeed once per app lifetime
                    instanceConfiguration.InitializeBaseUri(UriHelper.BuildAbsolute(request.Scheme, request.Host, instancePathBase, "/"));
                }
            }
            catch (Exception)
            {
                // Carry on. Any jobs depending on instance configuration will fail later if initialization was unsuccessful.
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
