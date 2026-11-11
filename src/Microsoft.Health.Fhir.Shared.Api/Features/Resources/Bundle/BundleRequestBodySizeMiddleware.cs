// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Logging.Metrics;

namespace Microsoft.Health.Fhir.Api.Features.Resources.Bundle
{
    /// <summary>
    /// Sets the request body size limit of bundle requests to <see cref="BundleConfiguration.MaxBundleSizeBytes"/>
    /// when size restrictions are enabled, so oversized bundles are rejected before the body is buffered or parsed.
    /// </summary>
    public sealed class BundleRequestBodySizeMiddleware
    {
        /// <summary>
        /// Request item holding the body size limit applied to a bundle request.
        /// </summary>
        public const string AppliedLimitItemKey = "Bundle.RequestBodySizeLimit";

        /// <summary>
        /// Request item set when a bundle request body exceeded the applied limit.
        /// </summary>
        public const string LimitExceededItemKey = "Bundle.RequestBodySizeLimitExceeded";

        private readonly RequestDelegate _next;
        private readonly BundleConfiguration _bundleConfiguration;
        private readonly IFailureMetricHandler _failureMetricHandler;
        private readonly ILogger<BundleRequestBodySizeMiddleware> _logger;
        private readonly string _pathBase;

        /// <summary>
        /// Initializes a new instance of the <see cref="BundleRequestBodySizeMiddleware"/> class.
        /// </summary>
        public BundleRequestBodySizeMiddleware(
            RequestDelegate next,
            IOptions<BundleConfiguration> bundleConfiguration,
            IOptions<FhirServerConfiguration> fhirServerConfiguration,
            IFailureMetricHandler failureMetricHandler,
            ILogger<BundleRequestBodySizeMiddleware> logger)
        {
            _next = EnsureArg.IsNotNull(next, nameof(next));
            _bundleConfiguration = EnsureArg.IsNotNull(bundleConfiguration?.Value, nameof(bundleConfiguration));
            _failureMetricHandler = EnsureArg.IsNotNull(failureMetricHandler, nameof(failureMetricHandler));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
            _pathBase = fhirServerConfiguration?.Value?.PathBase?.TrimEnd('/') ?? string.Empty;
        }

        /// <summary>
        /// Applies the bundle body size limit and records rejections.
        /// </summary>
        public async Task Invoke(HttpContext context)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            long limit = _bundleConfiguration.MaxBundleSizeBytes;

            if (!_bundleConfiguration.EnableSizeRestrictions || limit <= 0 || !IsBundleRequest(context.Request))
            {
                await _next(context);
                return;
            }

            IHttpMaxRequestBodySizeFeature bodySizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (bodySizeFeature == null || bodySizeFeature.IsReadOnly)
            {
                await _next(context);
                return;
            }

            bodySizeFeature.MaxRequestBodySize = limit;
            context.Items[AppliedLimitItemKey] = limit;

            await _next(context);

            if (context.Items.ContainsKey(LimitExceededItemKey))
            {
                _logger.LogWarning("Bundle rejected: request body of {ContentLength} bytes exceeds the limit of {Limit} bytes.", context.Request.ContentLength, limit);
                _failureMetricHandler.EmitException(new ExceptionMetricNotification
                {
                    OperationName = "Bundle",
                    ExceptionType = "BundleSizeLimitExceeded",
                    Severity = LogLevel.Warning.ToString(),
                });
            }
        }

        private bool IsBundleRequest(HttpRequest request)
        {
            if (!HttpMethods.IsPost(request.Method))
            {
                return false;
            }

            string path = request.Path.Value ?? string.Empty;
            if (_pathBase.Length > 0 && path.StartsWith(_pathBase, StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring(_pathBase.Length);
            }

            return path.Length == 0 || path == "/";
        }
    }
}
