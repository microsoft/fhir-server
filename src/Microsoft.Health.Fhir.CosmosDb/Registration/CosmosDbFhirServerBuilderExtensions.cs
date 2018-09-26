// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Azure.Documents;
using Microsoft.Extensions.Configuration;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Health;
using Microsoft.Health.Fhir.Core.Features.Initialization;
using Microsoft.Health.Fhir.Core.Features.Registration;
using Microsoft.Health.Fhir.CosmosDb.Configs;
using Microsoft.Health.Fhir.CosmosDb.Features.Health;
using Microsoft.Health.Fhir.CosmosDb.Features.Search;
using Microsoft.Health.Fhir.CosmosDb.Features.Search.Queries;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Versioning;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class CosmosDbFhirServerBuilderExtensions
    {
        public static IFhirServerBuilder AddCosmosDb(this IFhirServerBuilder fhirServerBuilder)
        {
            return fhirServerBuilder
                .AddCosmosDbPersistence()
                .AddCosmosDbSearch()
                .AddCosmosDbHealthCheck();
        }

        private static IFhirServerBuilder AddCosmosDbPersistence(this IFhirServerBuilder fhirServerBuilder)
        {
            fhirServerBuilder.Services.Add(provider =>
                {
                    var config = new CosmosDataStoreConfiguration();
                    provider.GetService<IConfiguration>().GetSection("CosmosDb").Bind(config);

                    if (string.IsNullOrEmpty(config.Host))
                    {
                        config.Host = CosmosDbLocalEmulator.Host;
                        config.Key = CosmosDbLocalEmulator.Key;
                    }

                    return config;
                })
                .Singleton()
                .AsSelf();

            fhirServerBuilder.Services.Add<CosmosDataStore>()
                .Scoped()
                .AsSelf()
                .AsImplementedInterfaces();

            fhirServerBuilder.Services.Add<DocumentClientProvider>()
                .Singleton()
                .AsSelf()
                .AsService<IStartable>() // so that it starts initializing ASAP
                .AsService<IRequireInitializationOnFirstRequest>(); // so that web requests block on its initialization.

            fhirServerBuilder.Services.Add<DocumentClientReadWriteTestProvider>()
                .Singleton()
                .AsService<IDocumentClientTestProvider>();

            // Register Func<IDocumentClient>.
            // We are intentionally not registering IDocumentClient directly, because
            // we want this codebase to support different configurations, where the
            // lifetime of the document clients can be managed outside of the IoC
            // container, which will automatically dispose it if exposed as a scoped
            // service or as transient but consumed from another scoped service.

            fhirServerBuilder.Services.Add<Func<IDocumentClient>>(sp => () => sp.GetService<DocumentClientProvider>().DocumentClient)
                .Singleton()
                .AsSelf();

            fhirServerBuilder.Services.Add<DocumentClientInitializer>()
                .Singleton()
                .AsService<IDocumentClientInitializer>();

            fhirServerBuilder.Services.Add<CosmosDocumentQueryFactory>()
                .Scoped()
                .AsService<ICosmosDocumentQueryFactory>();

            fhirServerBuilder.Services.Add<CosmosDocumentQueryLogger>()
                .Singleton()
                .AsService<ICosmosDocumentQueryLogger>();

            fhirServerBuilder.Services.Add<CollectionUpgradeManager>()
                .Singleton()
                .AsService<IUpgradeManager>();

            fhirServerBuilder.Services.TypesInSameAssemblyAs<ICollectionUpdater>()
                .AssignableTo<ICollectionUpdater>()
                .Singleton()
                .AsSelf()
                .AsService<ICollectionUpdater>();

            fhirServerBuilder.Services.AddSingleton<ICosmosDbDistributedLockFactory, CosmosDbDistributedLockFactory>();

            return fhirServerBuilder;
        }

        private static IFhirServerBuilder AddCosmosDbHealthCheck(this IFhirServerBuilder fhirServerBuilder)
        {
            // We can move to framework such as https://github.com/dotnet-architecture/HealthChecks
            // once they are released to do health check on multiple dependencies.
            fhirServerBuilder.Services.Add<CosmosHealthCheck>()
                .Scoped()
                .AsSelf()
                .AsService<IHealthCheck>();

            return fhirServerBuilder;
        }

        private static IFhirServerBuilder AddCosmosDbSearch(this IFhirServerBuilder fhirServerBuilder)
        {
            fhirServerBuilder.Services.Add<CosmosSearchService>()
                .Scoped()
                .AsSelf()
                .AsImplementedInterfaces();

            fhirServerBuilder.Services.AddSingleton<IQueryBuilder, QueryBuilder>();

            return fhirServerBuilder;
        }
    }
}
