// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
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
        /// <inheritdoc />
        public void Load(IServiceCollection services)
        {
            EnsureArg.IsNotNull(services, nameof(services));

            services.AddScoped<ResourceIdProvider>();

            services.AddSingleton<IRawResourceFactory, RawResourceFactory>();
            services.AddSingleton<IResourceWrapperFactory, ResourceWrapperFactory>();

            services.AddFactory<IScoped<ISearchService>>();
            services.AddFactory<IScoped<IFhirDataStore>>();
            services.AddFactory<IScoped<IFhirOperationDataStore>>();

            services.AddScoped<TransactionBundleValidator>();
            services.AddScoped<ResourceReferenceResolver>();

            services.AddFactory<IScoped<IDeletionService>>();
            services.AddScoped<IDeletionService, DeletionService>();
        }
    }
}
