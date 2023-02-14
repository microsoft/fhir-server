// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Http;

namespace Microsoft.Health.Fhir.Shared.Web
{
    public class UserAgentHeaderTelemetryInitializer : ITelemetryInitializer
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
                requestTelemetry.Context.User.UserAgent = _httpContextAccessor.HttpContext.Request.Headers["User-Agent"];
            }
        }
    }
}
