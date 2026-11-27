// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.ContentTypes;
using Microsoft.Health.Fhir.Api.Features.Formatters;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Logging.Metrics;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Api.Features.Resources.Bundle
{
    /// <summary>
    /// Sets the request body size limit of bundle requests to <see cref="BundleConfiguration.MaxBundleSizeBytes"/>
    /// when size restrictions are enabled, so oversized bundles are rejected with 413 before the body is buffered or parsed.
    /// </summary>
    public sealed class BundleRequestBodySizeMiddleware
    {
        /// <summary>
        /// Request item holding the body size limit applied to a bundle request.
        /// </summary>
        public const string AppliedLimitItemKey = "Bundle.RequestBodySizeLimit";

        private readonly RequestDelegate _next;
        private readonly BundleConfiguration _bundleConfiguration;
        private readonly IFailureMetricHandler _failureMetricHandler;
        private readonly IFormatParametersValidator _parametersValidator;
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
            IFormatParametersValidator parametersValidator,
            ILogger<BundleRequestBodySizeMiddleware> logger)
        {
            _next = EnsureArg.IsNotNull(next, nameof(next));
            _bundleConfiguration = EnsureArg.IsNotNull(bundleConfiguration?.Value, nameof(bundleConfiguration));
            _failureMetricHandler = EnsureArg.IsNotNull(failureMetricHandler, nameof(failureMetricHandler));
            _parametersValidator = EnsureArg.IsNotNull(parametersValidator, nameof(parametersValidator));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
            _pathBase = fhirServerConfiguration?.Value?.PathBase?.TrimEnd('/') ?? string.Empty;
        }

        /// <summary>
        /// Applies the bundle body size limit and returns 413 when it is exceeded.
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

            try
            {
                await _next(context);
            }
            catch (RequestEntityTooLargeException ex) when (!context.Response.HasStarted)
            {
                _logger.LogWarning("Bundle rejected: request body exceeds the limit of {Limit} bytes. Content-Length: {ContentLength}.", limit, context.Request.ContentLength);
                _failureMetricHandler.EmitException(new ExceptionMetricNotification
                {
                    OperationName = BundleHandler.BundleLimitOperationName,
                    ExceptionType = BundleHandler.BundleSizeExceededMetricType,
                    Severity = LogLevel.Warning.ToString(),
                });

                await WriteRequestEntityTooLargeAsync(context, ex);
            }
        }

        /// <summary>
        /// Converts Kestrel's body-too-large failure for a bundle with an applied limit into <see cref="RequestEntityTooLargeException"/>.
        /// </summary>
        internal static bool TryCreateBodyTooLargeException(HttpContext context, BadHttpRequestException exception, out RequestEntityTooLargeException tooLargeException)
        {
            tooLargeException = null;

            if (exception.StatusCode != StatusCodes.Status413PayloadTooLarge || !context.Items.TryGetValue(AppliedLimitItemKey, out object limit))
            {
                return false;
            }

            tooLargeException = new RequestEntityTooLargeException(string.Format(Api.Resources.BundleRequestBodyTooLarge, limit), exception);
            return true;
        }

        private async Task WriteRequestEntityTooLargeAsync(HttpContext context, RequestEntityTooLargeException exception)
        {
            context.Response.Clear();

            try
            {
                await _parametersValidator.CheckRequestedContentTypeAsync(context);
            }
            catch (UnsupportedMediaTypeException)
            {
                context.Response.ContentType = KnownContentTypes.JsonContentType;
            }

            var result = new OperationOutcomeResult(
                new OperationOutcome { Issue = exception.Issues.Select(issue => issue.ToPoco()).ToList() },
                HttpStatusCode.RequestEntityTooLarge,
                _logger);

            await result.ExecuteResultAsync(new ActionContext { HttpContext = context });
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
