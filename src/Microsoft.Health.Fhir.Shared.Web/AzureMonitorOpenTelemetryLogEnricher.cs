// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Api.Extensions;
using Microsoft.Health.Fhir.Core.Features.Telemetry;
using Microsoft.Health.Fhir.Core.Logging.Metrics;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace Microsoft.Health.Fhir.Shared.Web
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1515:Consider making public types internal", Justification = "Referenced by other assembles via shared projects.")]
    public sealed class AzureMonitorOpenTelemetryLogEnricher : BaseProcessor<LogRecord>
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IFailureMetricHandler _failureMetricHandler;

        public AzureMonitorOpenTelemetryLogEnricher(
            IHttpContextAccessor httpContextAccessor,
            IFailureMetricHandler failureMetricHandler)
        {
            EnsureArg.IsNotNull(httpContextAccessor, nameof(httpContextAccessor));
            EnsureArg.IsNotNull(failureMetricHandler, nameof(failureMetricHandler));

            _httpContextAccessor = httpContextAccessor;
            _failureMetricHandler = failureMetricHandler;
        }

        public override void OnEnd(LogRecord data)
        {
            if (data != null)
            {
                var newAttributes = new Dictionary<string, object?>();
                if (data.Attributes != null)
                {
                    foreach (var state in data.Attributes)
                    {
                        newAttributes.TryAdd(state.Key, state.Value);
                    }
                }

                AddOperationName(newAttributes);
                data.Attributes = newAttributes.ToList();

                EmitMetricBasedOnLogs(data);
            }

            base.OnEnd(data!);
        }

        public void AddOperationName(Dictionary<string, object?> attributes)
        {
            var request = _httpContextAccessor.HttpContext?.Request;
            if (request != null)
            {
                string name = request.GetOperationName();

                if (!string.IsNullOrWhiteSpace(name))
                {
                    attributes[KnownApplicationInsightsDimensions.OperationName] = name;
                }
            }
        }

        private void EmitMetricBasedOnLogs(LogRecord data)
        {
            // Metrics should be emitted if there is an exception, or if an error is logged.
            if (data.Exception != null || data.LogLevel == LogLevel.Error)
            {
                string operationName = string.Empty;
                var request = _httpContextAccessor.HttpContext?.Request;
                if (request != null)
                {
                    operationName = request.GetOperationName(includeRouteValues: false);
                }

                string exceptionType = data.Exception == null ? "ExceptionTypeNotDefined" : data.Exception.GetType().Name;
                var notification = new ExceptionMetricNotification()
                {
                    OperationName = operationName,
                    ExceptionType = exceptionType,
                    Severity = data.LogLevel.ToString(),
                };
                _failureMetricHandler.EmitException(notification);
            }
        }
    }
}
