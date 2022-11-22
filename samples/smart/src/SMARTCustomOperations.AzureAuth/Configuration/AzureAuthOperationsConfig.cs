// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Configuration;

namespace SMARTCustomOperations.AzureAuth.Configuration
{
    public class AzureAuthOperationsConfig
    {
        private string? _audience;
        private string? _apiManagementHostName;

        // Example: my-apim.azure-api.net
        public string? ApiManagementHostName
        {
            get
            {
                if (_apiManagementHostName is null)
                {
                    return null;
                }

                return _apiManagementHostName.Replace("https://", string.Empty, StringComparison.InvariantCultureIgnoreCase);
            }

            set
            {
                _apiManagementHostName = value;
            }
        }

        public string? ApiManagementFhirPrefex { get; set; } = "smart";

        // Returns more detailed error messages to client
        public bool Debug { get; set; } = false;

#pragma warning disable CA1056 // Needs to be string to parse from config easily.

        // Example: https://<workspace>-<fhir>.fhir.azurehealthcareapis.com
        public string? FhirServerUrl { get; set; }

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

        public string? ContextAppClientId { get; set; }

        // Only for static environment config service - not for production.
        public string? TestBackendClientId { get; set; }

        // Only for static environment config service - not for production.
        public string? TestBackendClientSecret { get; set; }

        // Only for static environment config service - not for production.
        public string? TestBackendClientJwks { get; set; }

        public void Validate()
        {
            if (string.IsNullOrEmpty(ApiManagementHostName))
            {
                throw new ConfigurationErrorsException("ApiManagementHostName must be configured for this application.");
            }

            if (string.IsNullOrEmpty(FhirServerUrl))
            {
                throw new ConfigurationErrorsException("FhirServerUrl must be configured for this application.");
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
