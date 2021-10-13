// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Registration;

namespace Microsoft.Health.Fhir.Import.DataStore.CosmosDb
{
    public static class ImportCosmosDbRegistrationExtensions
    {
        public static IFhirServerBuilder AddImportCosmosDbDataStore(this IFhirServerBuilder fhirServerBuilder)
        {
            EnsureArg.IsNotNull(fhirServerBuilder, nameof(fhirServerBuilder));
            IServiceCollection services = fhirServerBuilder.Services;

            services.Add<CosmosDbImporter>()
                .Transient()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<CosmosDbResourceMetaPopulator>()
                .Transient()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<CosmosDbSequenceIdGenerator>()
                .Transient()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<CosmosDbCompressedRawResourceConverter>()
                .Transient()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<CosmosImportOrchestratorTaskDataStoreOperation>()
                .Transient()
                .AsSelf()
                .AsImplementedInterfaces();

            return fhirServerBuilder;
        }
    }
}
