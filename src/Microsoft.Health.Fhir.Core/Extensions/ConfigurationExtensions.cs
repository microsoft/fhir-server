// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Registration;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class ConfigurationExtensions
    {
        public static FhirRuntimeState GetRuntimeStateConfiguration(string configuredRuntimeState)
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
