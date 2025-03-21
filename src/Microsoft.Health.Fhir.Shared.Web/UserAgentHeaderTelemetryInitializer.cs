﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Microsoft.Health.Fhir.Shared.Web
{
    internal sealed class UserAgentHeaderTelemetryInitializer : ITelemetryInitializer
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public UserAgentHeaderTelemetryInitializer(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public void Initialize(ITelemetry telemetry)
        {
            if (telemetry is RequestTelemetry requestTelemetry)
            {
                if (_httpContextAccessor.HttpContext.Request.Headers.TryGetValue(HeaderNames.UserAgent, out var userAgent))
                {
                    string propertyName = HeaderNames.UserAgent.Replace('-', '_').ToLower(CultureInfo.InvariantCulture);
                    requestTelemetry.Properties.Add(propertyName, userAgent);
                }
            }
        }
    }
}
