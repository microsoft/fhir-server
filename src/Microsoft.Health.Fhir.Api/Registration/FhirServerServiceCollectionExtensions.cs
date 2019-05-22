// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;
using EnsureThat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Configs;

namespace Microsoft.Health.Fhir.Api.Registration
{
    public static class FhirServerServiceCollectionExtensions
    {
        public static IServiceCollection AddFhirServerBase(this IServiceCollection services, FhirServerConfiguration fhirServerConfiguration)
        {
            EnsureArg.IsNotNull(services, nameof(services));
            EnsureArg.IsNotNull(fhirServerConfiguration, nameof(fhirServerConfiguration));

            services.RegisterAssemblyModules(Assembly.GetExecutingAssembly(), fhirServerConfiguration);

            return services;
        }
    }
}
