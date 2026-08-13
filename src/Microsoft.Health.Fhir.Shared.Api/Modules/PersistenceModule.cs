// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.Resources.Bundle;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Resources;
using Microsoft.Health.Fhir.Core.Features.Search;
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
        /// Initializes a new instance of the <see cref="PersistenceModule"/> class.
        /// </summary>
        /// <param name="fhirServerConfiguration">The FHIR server configuration, used to select the configured FHIR SDK provider.</param>
        public PersistenceModule(FhirServerConfiguration fhirServerConfiguration)
        {
            EnsureArg.IsNotNull(fhirServerConfiguration, nameof(fhirServerConfiguration));

            _fhirSdkProvider = fhirServerConfiguration.CoreFeatures.FhirSdkProvider.EffectiveSerialization;
        }

        /// <inheritdoc />
        public void Load(IServiceCollection services)
        {
            EnsureArg.IsNotNull(services, nameof(services));

            services.AddScoped<ResourceIdProvider>();

            services.AddSingleton<RawResourceFactory>();

            switch (_fhirSdkProvider)
            {
                case FhirSdkProvider.Firely:
                    services.AddSingleton<IRawResourceFactory>(sp => sp.GetRequiredService<RawResourceFactory>());
                    break;

                case FhirSdkProvider.Ignixa:
                    // The Firely factory stays reachable as the implementation for resources that did not come
                    // from Ignixa's parser and therefore have no JSON document to serialize from.
                    services.AddSingleton<IRawResourceFactory>(
                        sp => new IgnixaRawResourceFactory(sp.GetRequiredService<RawResourceFactory>()));
                    break;

                default:
                    throw new InvalidOperationException($"Unsupported FHIR SDK provider: {_fhirSdkProvider}.");
            }

            services.AddSingleton<IResourceWrapperFactory, ResourceWrapperFactory>();

            services.AddFactory<IScoped<ISearchService>>();
            services.AddFactory<IScoped<IFhirDataStore>>();
            services.AddFactory<IScoped<IFhirOperationDataStore>>();

            services.AddScoped<TransactionBundleValidator>();
            services.AddScoped<ResourceReferenceResolver>();

            services.AddFactory<IScoped<IDeletionService>>();
            services.AddScoped<IDeletionService, DeletionService>();

            services.AddFactory<IScoped<IBulkUpdateService>>();
            services.AddScoped<IBulkUpdateService, BulkUpdateService>();
        }
    }
}
