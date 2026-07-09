// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Api.Features.Resources.Bundle;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Resources;
using Microsoft.Health.Fhir.Core.Features.Search;

namespace Microsoft.Health.Fhir.Api.Modules
{
    /// <summary>
    /// Registration of data persistence components
    /// </summary>
    /// <seealso cref="IStartupModule" />
    public class PersistenceModule : IStartupModule
    {
        private readonly FhirServerConfiguration _configuration;

        /// <summary>
        /// Initializes a new instance of the <see cref="PersistenceModule"/> class.
        /// </summary>
        /// <param name="configuration">The FHIR server configuration.</param>
        public PersistenceModule(FhirServerConfiguration configuration)
        {
            _configuration = EnsureArg.IsNotNull(configuration, nameof(configuration));
        }

        /// <inheritdoc />
        public void Load(IServiceCollection services)
        {
            EnsureArg.IsNotNull(services, nameof(services));

            services.AddScoped<ResourceIdProvider>();

            if (_configuration.Sdk.Mode == FhirSdkMode.Firely)
            {
                services.AddSingleton<IRawResourceFactory, FirelyRawResourceFactory>();
            }
            else if (_configuration.Sdk.Mode == FhirSdkMode.Ignixa)
            {
                services.AddSingleton<IRawResourceFactory, IgnixaModeRawResourceFactory>();
            }
            else
            {
                services.AddSingleton<IRawResourceFactory, RawResourceFactory>();
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
