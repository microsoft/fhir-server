// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using EnsureThat;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Features.Telemetry;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace Microsoft.Health.Fhir.Shared.Web
{
    public class AzureMonitorOpenTelemetryLogEnricher : BaseProcessor<LogRecord>
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AzureMonitorOpenTelemetryLogEnricher(IHttpContextAccessor httpContextAccessor)
        {
            EnsureArg.IsNotNull(httpContextAccessor, nameof(httpContextAccessor));

            _httpContextAccessor = httpContextAccessor;
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
            }

            base.OnEnd(data!);
        }

        public void AddOperationName(Dictionary<string, object?> attributes)
        {
            var request = _httpContextAccessor.HttpContext?.Request;
            if (request != null)
            {
                var name = request.Path.Value;
                if (request.RouteValues != null
                    && request.RouteValues.TryGetValue(KnownHttpRequestProperties.RouteValueAction, out var action)
                    && request.RouteValues.TryGetValue(KnownHttpRequestProperties.RouteValueController, out var controller))
                {
                    name = $"{controller}/{action}";
                    var parameterArray = request.RouteValues.Keys?.Where(
                        k => k.Contains(KnownHttpRequestProperties.RouteValueParameterSuffix, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    if (parameterArray != null && parameterArray.Any())
                    {
                        name += $" [{string.Join("/", parameterArray)}]";
                    }
                }

                if (!string.IsNullOrWhiteSpace(name))
                {
                    attributes[KnownApplicationInsightsDimensions.OperationName] = $"{request.Method} {name}";
                }
            }
        }
    }
}
