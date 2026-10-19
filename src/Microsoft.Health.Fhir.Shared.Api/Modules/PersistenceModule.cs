// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Features.Resources.Bundle;
using Microsoft.Health.Fhir.Core.Extensions;
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

            services.AddScopedFactory<ISearchService>();
            services.AddScopedFactory<IFhirDataStore>();
            services.AddScopedFactory<IFhirOperationDataStore>();

            services.AddScoped<TransactionBundleValidator>();
            services.AddScoped<ResourceReferenceResolver>();

            services.AddScopedFactory<IDeletionService>();
            services.AddScoped<IDeletionService, DeletionService>();

            services.AddScopedFactory<IBulkUpdateService>();
            services.AddScoped<IBulkUpdateService, BulkUpdateService>();
        }
    }
}
