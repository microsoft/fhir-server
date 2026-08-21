// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Registration;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class RuntimeStateConfigurationExtensions
    {
        private const string RuntimeStateConfigurationKey = "FhirServer:CoreFeatures:RuntimeState";

        /// <summary>
        /// Gets the FHIR runtime state configuration.
        /// </summary>
        /// <param name="configuration">The configuration instance.</param>
        /// <returns>The FHIR runtime state.</returns>
        /// <remarks>Active is assumed as the default runtime state for invalid inputs.</remarks>
        public static FhirRuntimeState GetRuntimeStateConfiguration(IConfiguration configuration)
        {
            string configuredRuntimeState = configuration[RuntimeStateConfigurationKey];

            return ParseRuntimeStateConfiguration(configuredRuntimeState);
        }

        /// <summary>
        /// Gets the FHIR runtime state configuration.
        /// </summary>
        /// <remarks>Active is assumed as the default runtime state for invalid inputs.</remarks>
        public static FhirRuntimeState GetRuntimeStateConfiguration(CoreFeatureConfiguration coreFeatureConfiguration)
        {
            return ParseRuntimeStateConfiguration(coreFeatureConfiguration?.RuntimeState);
        }

        /// <summary>
        /// Parses the FHIR runtime state configuration.
        /// </summary>
        /// <remarks>Active is assumed as the default runtime state for invalid inputs.</remarks>
        public static FhirRuntimeState ParseRuntimeStateConfiguration(string configuredRuntimeState)
        {
            if (string.IsNullOrWhiteSpace(configuredRuntimeState))
            {
                return FhirRuntimeState.Active;
            }

            string normalizedRuntimeState = configuredRuntimeState.Trim();
            if (Enum.TryParse(normalizedRuntimeState, ignoreCase: true, out FhirRuntimeState runtimeState) &&
                Enum.IsDefined(runtimeState) &&
                string.Equals(normalizedRuntimeState, runtimeState.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return runtimeState;
            }

            return FhirRuntimeState.Active;
        }
    }
}
