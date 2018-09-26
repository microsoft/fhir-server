// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Reflection;
using EnsureThat;
using Microsoft.Extensions.Configuration;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Core.Registration;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class FhirServerServiceCollectionExtensions
    {
        private const string FhirServerConfigurationSectionName = "FhirServer";

        /// <summary>
        /// Adds services for enabling a FHIR server.
        /// </summary>
        /// <param name="services">The services collection.</param>
        /// <param name="configurationRoot">An optional configuration root object. This method uses "FhirServer" section.</param>
        /// <param name="configureAction">An optional delegate to set <see cref="FhirServerConfiguration"/> properties after values have been loaded from configuration</param>
        /// <returns>A <see cref="IFhirServerBuilder"/> object.</returns>
        public static IFhirServerBuilder AddFhirServer(
            this IServiceCollection services,
            IConfiguration configurationRoot = null,
            Action<FhirServerConfiguration> configureAction = null)
        {
            EnsureArg.IsNotNull(services, nameof(services));

            services.AddOptions();
            services.AddMvc(options =>
            {
                options.RespectBrowserAcceptHeader = true;
            });

            var fhirServerConfiguration = new FhirServerConfiguration();

            configurationRoot?.GetSection(FhirServerConfigurationSectionName).Bind(fhirServerConfiguration);
            configureAction?.Invoke(fhirServerConfiguration);

            services.AddSingleton(Microsoft.Extensions.Options.Options.Create(fhirServerConfiguration));
            services.AddSingleton(Microsoft.Extensions.Options.Options.Create(fhirServerConfiguration.Security));
            services.AddSingleton(Microsoft.Extensions.Options.Options.Create(fhirServerConfiguration.Conformance));
            services.AddSingleton(Microsoft.Extensions.Options.Options.Create(fhirServerConfiguration.Features));
            services.AddSingleton(Microsoft.Extensions.Options.Options.Create(fhirServerConfiguration.Search));

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
