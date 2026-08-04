// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics.Metrics;
using System.Net.Http;
using EnsureThat;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Tenancy;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Api.Registration
{
    /// <summary>
    /// Registers the optional multi-tenant hosting services for the FHIR server.
    /// </summary>
    public static class FhirServerTenancyRegistrationExtensions
    {
        private static readonly TimeSpan MinimumPeriodicTimerInterval = TimeSpan.FromMilliseconds(1);
        private static readonly TimeSpan MaximumPeriodicTimerInterval = TimeSpan.FromMilliseconds(uint.MaxValue - 1d);
        private static readonly string TenantContainerSweeperTypeName = typeof(TenantContainerSweeper).FullName!;

        /// <summary>
        /// Registers multi-tenant hosting services when tenancy is enabled.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configuration">The tenancy configuration.</param>
        /// <returns>The original collection, for chaining.</returns>
        /// <remarks>
        /// The registered <see cref="ITenantServiceBlueprint"/> holds a live reference to
        /// <paramref name="services"/> and creates snapshots on demand, so registrations added later are
        /// still visible to tenant containers.
        /// </remarks>
        public static IServiceCollection AddFhirServerTenancy(
            this IServiceCollection services,
            TenancyConfiguration configuration)
        {
            EnsureArg.IsNotNull(services, nameof(services));
            EnsureArg.IsNotNull(configuration, nameof(configuration));

            if (!configuration.Enabled)
            {
                return services;
            }

            ValidateEnabledConfiguration(configuration);

            services.TryAddSingleton(TimeProvider.System);
            services.AddSingleton(CreateCacheOptions(configuration));
            services.AddSingleton(CreateDefaultSharedServiceRegistry());
            services.AddSingleton<ITenantHostedServicePolicy>(CreateHostedServicePolicy());
            services.AddSingleton<ITenantServiceConfigurator, TenantInstanceConfigurationConfigurator>();
            AddSingletonAlias<TenantContainerCache, ITenantContainerCache>(services);
            services.AddSingleton<ITenantContainerFactory, TenantContainerFactory>();
            services.AddSingleton<IHostedService, TenantContainerSweeper>();
            services.AddSingleton<ITenantServiceBlueprint>(new TenantServiceBlueprint(services));

            return services;
        }

        private static void AddSingletonAlias<TImplementation, TService>(IServiceCollection services)
            where TImplementation : class, TService
            where TService : class
        {
            services.AddSingleton<TImplementation>();
            services.AddSingleton<TService>(static serviceProvider => serviceProvider.GetRequiredService<TImplementation>());
        }

        private static IOptions<TenantContainerCacheOptions> CreateCacheOptions(TenancyConfiguration configuration)
        {
            return Options.Create(new TenantContainerCacheOptions
            {
                MaxResidentTenants = configuration.MaxResidentTenants,
                IdleTimeout = configuration.IdleTimeout,
                SweepInterval = configuration.SweepInterval,
            });
        }

        private static TenantSharedServiceRegistry CreateDefaultSharedServiceRegistry()
        {
            var registry = new TenantSharedServiceRegistry();

            registry.ShareWithTenants<ILoggerFactory>();
            registry.ShareWithTenants<ILoggerProvider>();
            registry.ShareWithTenants<IConfiguration>();
            registry.ShareWithTenants<IHostEnvironment>();
            registry.ShareWithTenants<IWebHostEnvironment>();
            registry.ShareWithTenants<IHostApplicationLifetime>();
            registry.ShareWithTenants<IHttpClientFactory>();
            registry.ShareWithTenants<IMeterFactory>();

            registry.ShareWithTenants<IAuditEventTypeMapping>();
            registry.ShareWithTenants<IModelInfoProvider>();
            registry.ShareWithTenants<ICompartmentDefinitionManager>();
            registry.ShareWithTenants<ISearchParameterDefinitionSource>();

            // ILogger<T> resolves from ILoggerFactory, IServer remains intentionally unshared, and
            // TelemetryClient is intentionally omitted because this project does not carry an
            // Application Insights dependency.

            return registry;
        }

        private static TenantHostedServicePolicy CreateHostedServicePolicy()
        {
            var hostedServicePolicy = new TenantHostedServicePolicy();
            hostedServicePolicy.Set(TenantContainerSweeperTypeName, TenantHostedServiceDisposition.Shared);

            return hostedServicePolicy;
        }

        private static void ValidateEnabledConfiguration(TenancyConfiguration configuration)
        {
            EnsureArg.IsGt(
                configuration.MaxResidentTenants,
                0,
                nameof(TenancyConfiguration.MaxResidentTenants));
            EnsureArg.IsGt(
                configuration.IdleTimeout,
                TimeSpan.Zero,
                nameof(TenancyConfiguration.IdleTimeout));
            EnsureArg.IsGte(
                configuration.SweepInterval,
                MinimumPeriodicTimerInterval,
                nameof(TenancyConfiguration.SweepInterval));
            EnsureArg.IsLte(
                configuration.SweepInterval,
                MaximumPeriodicTimerInterval,
                nameof(TenancyConfiguration.SweepInterval));
        }
    }
}
