// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.CosmosDb.Features.Storage;
using Microsoft.Health.CosmosDb.Features.Storage.StoredProcedures;
using Microsoft.Health.CosmosDb.Features.Storage.Versioning;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Health;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.CosmosDb.Features.Health;
using Microsoft.Health.Fhir.CosmosDb.Features.Search;
using Microsoft.Health.Fhir.CosmosDb.Features.Search.Queries;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.StoredProcedures;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Versioning;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class FhirServerBuilderCosmosDbRegistrationExtensions
    {
        /// <summary>
        /// Adds Cosmos Db as the data store for the FHIR server.
        /// </summary>
        /// <param name="fhirServerBuilder">The FHIR server builder.</param>
        /// <returns>The builder.</returns>
        public static IFhirServerBuilder AddFhirServerCosmosDb(this IFhirServerBuilder fhirServerBuilder)
        {
            EnsureArg.IsNotNull(fhirServerBuilder, nameof(fhirServerBuilder));

            return fhirServerBuilder
                .AddCosmosDbPersistence()
                .AddCosmosDbSearch()
                .AddCosmosDbHealthCheck();
        }

        private static IFhirServerBuilder AddCosmosDbPersistence(this IFhirServerBuilder fhirServerBuilder)
        {
            IServiceCollection services = fhirServerBuilder.Services;

            services.Add<FhirDataStore>()
                .Scoped()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<FhirCollectionUpgradeManager>()
                .Singleton()
                .AsSelf()
                .AsService<IUpgradeManager>();

            services.Add<FhirDocumentQueryLogger>()
                .Singleton()
                .AsService<IFhirDocumentQueryLogger>();

            services.Add<FhirCollectionInitializer>()
                .Singleton()
                .AsSelf()
                .AsService<ICollectionInitializer>();

            services.Add<FhirCollectionSettingsUpdater>()
                .Singleton()
                .AsService<IFhirCollectionUpdater>();

            services.Add<FhirStoredProcedureInstaller>()
                .Singleton()
                .AsService<IFhirCollectionUpdater>();

            services.TypesInSameAssemblyAs<IFhirStoredProcedure>()
                .AssignableTo<IStoredProcedure>()
                .Singleton()
                .AsSelf()
                .AsService<IFhirStoredProcedure>();

            services.Add<FhirCosmosDocumentQueryFactory>()
                .Singleton()
                .AsSelf();

            return fhirServerBuilder;
        }

        private static IFhirServerBuilder AddCosmosDbHealthCheck(this IFhirServerBuilder fhirServerBuilder)
        {
            // We can move to framework such as https://github.com/dotnet-architecture/HealthChecks
            // once they are released to do health check on multiple dependencies.
            fhirServerBuilder.Services.Add<FhirCosmosHealthCheck>()
                .Scoped()
                .AsSelf()
                .AsService<IHealthCheck>();

            return fhirServerBuilder;
        }

        private static IFhirServerBuilder AddCosmosDbSearch(this IFhirServerBuilder fhirServerBuilder)
        {
            fhirServerBuilder.Services.Add<FhirCosmosSearchService>()
                .Scoped()
                .AsSelf()
                .AsImplementedInterfaces();

            fhirServerBuilder.Services.AddSingleton<IQueryBuilder, QueryBuilder>();

            return fhirServerBuilder;
        }
    }
}
