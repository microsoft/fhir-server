// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.Resources.Bundle;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Resources;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Ignixa;
using Microsoft.Health.Fhir.Ignixa.Features.Persistence;

namespace Microsoft.Health.Fhir.Api.Modules
{
    /// <summary>
    /// Registration of data persistence components
    /// </summary>
    /// <seealso cref="IStartupModule" />
    public class PersistenceModule : IStartupModule
    {
        private readonly FhirSdkProvider _fhirSdkProvider;

        /// <summary>
        /// Selects the effective persistence write provider at startup.
        /// </summary>
        /// <param name="fhirServerConfiguration">The FHIR server configuration.</param>
        public PersistenceModule(FhirServerConfiguration fhirServerConfiguration)
        {
            EnsureArg.IsNotNull(fhirServerConfiguration, nameof(fhirServerConfiguration));

            _fhirSdkProvider = fhirServerConfiguration.CoreFeatures.FhirSdkProvider.EffectivePersistenceWrite;
        }

        /// <inheritdoc />
        public void Load(IServiceCollection services)
        {
            EnsureArg.IsNotNull(services, nameof(services));

            services.AddScoped<ResourceIdProvider>();

            switch (_fhirSdkProvider)
            {
                case FhirSdkProvider.Firely:
                    services.AddSingleton<IRawResourceFactory, RawResourceFactory>();
                    break;

                case FhirSdkProvider.Ignixa:
                    services.TryAddSingleton<IgnixaSchemaContext>();
                    services.AddSingleton<IRawResourceFactory>(sp => new IgnixaRawResourceFactory(
                        ActivatorUtilities.CreateInstance<RawResourceFactory>(sp),
                        sp.GetRequiredService<IgnixaSchemaContext>(),
                        sp.GetRequiredService<IModelInfoProvider>()));
                    break;

                default:
                    throw new InvalidOperationException($"Unsupported FHIR SDK provider for PersistenceWrite: {_fhirSdkProvider}.");
            }

            services.AddHostedService<PersistenceWriterActivator>();
            services.AddScoped<IResourceWrapperFactory, ResourceWrapperFactory>();

            services.AddScoped<TransactionBundleValidator>();
            services.AddScoped<ResourceReferenceResolver>();

            services.AddScoped<IDeletionService, DeletionService>();

            services.AddScoped<IBulkUpdateService, BulkUpdateService>();
        }

        /// <summary>Activates the persistence writer at startup so configuration failures surface before the first write.</summary>
        private sealed class PersistenceWriterActivator : IHostedService
        {
            public PersistenceWriterActivator(IRawResourceFactory rawResourceFactory)
            {
                EnsureArg.IsNotNull(rawResourceFactory, nameof(rawResourceFactory));
            }

            public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

            public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }
    }
}
