// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;
using EnsureThat;
using Microsoft.Extensions.Configuration;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Core.Features.Registration;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class FhirServerServiceCollectionExtensions
    {
        public static IFhirServerBuilder AddFhirServer(this IServiceCollection services, IConfiguration configurationRoot)
        {
            EnsureArg.IsNotNull(services, nameof(services));

            services.AddOptions();
            services.AddMvc(options =>
            {
                options.RespectBrowserAcceptHeader = true;
            });

            var fhirServerConfiguration = new FhirServerConfiguration();

            configurationRoot.GetSection("FhirServer").Bind(fhirServerConfiguration);

            services.AddSingleton(Options.Options.Create(fhirServerConfiguration));
            services.AddSingleton(Options.Options.Create(fhirServerConfiguration.Security));
            services.AddSingleton(Options.Options.Create(fhirServerConfiguration.Conformance));
            services.AddSingleton(Options.Options.Create(fhirServerConfiguration.Features));
            services.AddSingleton(Options.Options.Create(fhirServerConfiguration.Search));

            services.RegisterAssemblyModules(Assembly.GetExecutingAssembly(), fhirServerConfiguration);

            return new FhirServerBuilder(services);
        }

        private class FhirServerBuilder : IFhirServerBuilder
        {
            public FhirServerBuilder(IServiceCollection services)
            {
                EnsureArg.IsNotNull(services, nameof(services));
                Services = services;
            }

            public IServiceCollection Services { get; }
        }
    }
}
