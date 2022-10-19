// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace SMARTProxy.Configuration
{
    public class SMARTProxyConfig
    {
#pragma warning disable CA1056 // Needs to be string to parse from config easily.
        public string? FhirServerUrl { get; set; }
#pragma warning restore CA1056 // Needs to be string to parse from config easily.

        public string? AppInsightsConnectionString { get; set; }

        public string? AppInsightsInstrumentationKey { get; set; }

        public string? TenantId { get; set; }

        public string? Audience { get; set; }

        public void Validate()
        {
            if (string.IsNullOrEmpty(TenantId))
            {
                throw new ArgumentException("TenantId must be configured for this application.");
            }

            if (string.IsNullOrEmpty(Audience))
            {
                throw new ArgumentException("Audience must be configured for this application.");
            }
        }
    }
}
