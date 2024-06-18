// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.Health.Fhir.Api.Extensions;
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
                string name = request.GetOperationName();

                if (!string.IsNullOrWhiteSpace(name))
                {
                    attributes[KnownApplicationInsightsDimensions.OperationName] = name;
                }
            }
        }
    }
}
