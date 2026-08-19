// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.AspNetCore.Builder;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.RuntimeState;
using Microsoft.Health.Fhir.Core.Registration;

namespace Microsoft.Health.Fhir.Api.Features.Context
{
    public static class FhirRequestRuntimeStateMiddleware
    {
        public static IApplicationBuilder UseFhirRuntimeState(this IApplicationBuilder builder, FhirServerConfiguration fhirServerConfiguration)
        {
            EnsureArg.IsNotNull(fhirServerConfiguration, nameof(fhirServerConfiguration));

            // For Azure API for FHIR, runtime state can be 'Active' or 'Deprecated'.
            // For AHDS FHIR, runtime state is always 'Active'. Even if the configuration is set as 'Deprecated', it will be ignored and treated as 'Active'.
            // That logic is part of 'AzureHealthDataServicesRuntimeConfiguration' as a second of protection.
            if (Core.Extensions.ConfigurationExtensions.GetRuntimeStateConfiguration(fhirServerConfiguration.CoreFeatures) == FhirRuntimeState.Deprecated)
            {
                builder.UseMiddleware<RuntimeStateMiddleware>();
            }

            return builder;
        }
    }
}
