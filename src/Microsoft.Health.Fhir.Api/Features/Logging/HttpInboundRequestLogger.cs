// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features;

namespace Microsoft.Health.Fhir.Api.Features.Logging
{
    /// <summary>
    /// Logs an inbound request using the same structured record as the R9 HTTP logging middleware.
    /// </summary>
    /// <remarks>
    /// The Geneva exporter maps this logger's category to the FhirInboundRequestLog table.
    /// Timestamps, environment, trace, exception, and Kubernetes dimensions are supplied by
    /// the OpenTelemetry logging pipeline.
    /// </remarks>
    public sealed class HttpInboundRequestLogger : IHttpInboundRequestLogger
    {
        internal const string DurationColumnName = "duration";
        internal const string HttpHostColumnName = "httpHost";
        internal const string HttpMethodColumnName = "httpMethod";
        internal const string HttpPathColumnName = "httpPath";
        internal const string HttpStatusCodeColumnName = "httpStatusCode";
        internal const string XCorrelationIdColumnName = "XCorrelationId";

        private static readonly EventId RequestCompletedEventId = new EventId(1, "RequestCompleted");
        private static readonly EventId RequestProcessingErrorEventId = new EventId(2, "RequestProcessingError");

        private readonly ILogger<HttpInboundRequestLogger> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="HttpInboundRequestLogger"/> class.
        /// </summary>
        /// <param name="logger">The logger that emits entries under the request logger category.</param>
        public HttpInboundRequestLogger(ILogger<HttpInboundRequestLogger> logger)
        {
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        /// <summary>
        /// Logs the current HTTP request and response dimensions.
        /// </summary>
        /// <remarks>
        /// Call this method after assigning the final response status on a terminal middleware
        /// branch that will not reach the R9 HTTP logging middleware.
        /// </remarks>
        /// <param name="context">The HTTP context to log.</param>
        /// <param name="exception">The exception associated with the request, if any.</param>
        public void LogRequest(HttpContext context, Exception exception = null)
        {
            EnsureArg.IsNotNull(context, nameof(context));

            try
            {
                int httpStatusCode = context.Response?.StatusCode ?? 0;

                IReadOnlyList<KeyValuePair<string, object>> state =
                [
                    new KeyValuePair<string, object>(DurationColumnName, GetElapsedMilliseconds(context)),
                    new KeyValuePair<string, object>(HttpHostColumnName, SanitizeForLog(context.Request?.Host.Value)),
                    new KeyValuePair<string, object>(HttpMethodColumnName, SanitizeForLog(context.Request?.Method)),
                    new KeyValuePair<string, object>(HttpPathColumnName, SanitizeForLog(context.Request?.Path.Value)),
                    new KeyValuePair<string, object>(HttpStatusCodeColumnName, httpStatusCode),
                    new KeyValuePair<string, object>(XCorrelationIdColumnName, GetCorrelationId(context)),
                ];

                _logger.Log(
                    httpStatusCode >= 500 && exception != null ? LogLevel.Error : LogLevel.Information,
                    exception != null ? RequestProcessingErrorEventId : RequestCompletedEventId,
                    state,
                    exception,
                    FormatLogMessage);
            }
            catch (Exception e)
            {
                // Generic catch to intercept any logging errors and avoid breaking the request pipeline. The logger should not throw exceptions.
                _logger.LogError(e, "An error occurred while logging the HTTP Inbound request.");
            }
        }

        private static string GetCorrelationId(HttpContext context)
        {
            // X-Correlation-ID is not always present in the request headers, so we need to check for its existence before attempting to retrieve it.
            if (context.Request.Headers.TryGetValue(KnownHeaders.CorrelationId, out var correlationId) && !string.IsNullOrEmpty(correlationId))
            {
                return SanitizeForLog(correlationId);
            }

            return string.Empty;
        }

        private static long GetElapsedMilliseconds(HttpContext context)
        {
            Activity activity = context.Features.Get<IHttpActivityFeature>()?.Activity;
            if (activity is null)
            {
                return 0;
            }

            TimeSpan duration = activity.Duration == TimeSpan.Zero
                ? DateTime.UtcNow - activity.StartTimeUtc
                : activity.Duration;

            return Math.Max(0, (long)duration.TotalMilliseconds);
        }

        private static string SanitizeForLog(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value
                .Replace("\r", string.Empty, StringComparison.Ordinal)
                .Replace("\n", string.Empty, StringComparison.Ordinal);
        }

        private static string FormatLogMessage(
            IReadOnlyList<KeyValuePair<string, object>> state,
            Exception exception)
        {
            return exception is null ? string.Empty : "HTTP request processing failed.";
        }
    }
}
