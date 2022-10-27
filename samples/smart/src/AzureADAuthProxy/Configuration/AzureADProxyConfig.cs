// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Configuration;

namespace AzureADAuthProxy.Configuration
{
    public class AzureADProxyConfig
    {
        private string? _audience;

#pragma warning disable CA1056 // Needs to be string to parse from config easily.
        public string? SmartFhirEndpoint { get; set; }

        public string? BackendFhirUrl { get; set; }
#pragma warning restore CA1056 // Needs to be string to parse from config easily.

        public string? AppInsightsConnectionString { get; set; }

        public string? AppInsightsInstrumentationKey { get; set; }

        public string? TenantId { get; set; }

        public string? Audience
        {
            get => _audience;
            set
            {
                if (value is not null && value.Length > 0)
                {
                    if (!value.EndsWith("/", StringComparison.InvariantCultureIgnoreCase))
                    {
                        _audience = value + "/";
                    }
                    else
                    {
                        _audience = value;
                    }
                }
            }
        }

        public string? BackendServiceKeyVaultStore { get; set; }

        public string? TestBackendClientId { get; set; }

        public string? TestBackendClientSecret { get; set; }

        public string? TestBackendClientJwks { get; set; }

        public void Validate()
        {
            if (string.IsNullOrEmpty(SmartFhirEndpoint))
            {
                throw new ConfigurationErrorsException("SmartFhirEndpoint must be configured for this application.");
            }

            if (string.IsNullOrEmpty(BackendFhirUrl))
            {
                throw new ConfigurationErrorsException("BackendFhirUrl must be configured for this application.");
            }

            if (string.IsNullOrEmpty(TenantId))
            {
                throw new ConfigurationErrorsException("TenantId must be configured for this application.");
            }

            if (string.IsNullOrEmpty(Audience))
            {
                throw new ConfigurationErrorsException("Audience must be configured for this application.");
            }
        }
    }
}
