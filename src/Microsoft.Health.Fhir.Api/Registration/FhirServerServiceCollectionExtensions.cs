// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;
using EnsureThat;
using Microsoft.Health.Api.Modules;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Subscriptions.Registration;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class FhirServerServiceCollectionExtensions
    {
        public static IServiceCollection AddFhirServerBase(this IServiceCollection services, FhirServerConfiguration fhirServerConfiguration)
        {
            EnsureArg.IsNotNull(services, nameof(services));
            EnsureArg.IsNotNull(fhirServerConfiguration, nameof(fhirServerConfiguration));

            services.RegisterAssemblyModules(Assembly.GetExecutingAssembly(), fhirServerConfiguration);
            services.RegisterAssemblyModules(typeof(InitializationModule).Assembly, fhirServerConfiguration);
            services.RegisterAssemblyModules(typeof(SubscriptionsModule).Assembly, fhirServerConfiguration);

            return services;
        }
    }
}
