// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.CosmosDb.Configs;
using Microsoft.Health.CosmosDb.Features.Storage;
using Microsoft.Health.CosmosDb.Features.Storage.StoredProcedures;
using Microsoft.Health.CosmosDb.Features.Storage.Versioning;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.CosmosDb;
using Microsoft.Health.Fhir.CosmosDb.Features.Health;
using Microsoft.Health.Fhir.CosmosDb.Features.Search;
using Microsoft.Health.Fhir.CosmosDb.Features.Search.Queries;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Operations;
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
        /// <param name="configuration">The configuration for the server</param>
        /// <returns>The builder.</returns>
        public static IFhirServerBuilder AddCosmosDb(this IFhirServerBuilder fhirServerBuilder, IConfiguration configuration)
        {
            EnsureArg.IsNotNull(fhirServerBuilder, nameof(fhirServerBuilder));
            EnsureArg.IsNotNull(configuration, nameof(configuration));

            return fhirServerBuilder
                .AddCosmosDbPersistence(configuration)
                .AddCosmosDbSearch()
                .AddCosmosDbHealthCheck();
        }

        private static IFhirServerBuilder AddCosmosDbPersistence(this IFhirServerBuilder fhirServerBuilder, IConfiguration configuration)
        {
            IServiceCollection services = fhirServerBuilder.Services;

            services.AddCosmosDb();

            services.Configure<CosmosCollectionConfiguration>(Constants.CollectionConfigurationName, cosmosCollectionConfiguration => configuration.GetSection("FhirServer:CosmosDb").Bind(cosmosCollectionConfiguration));

            services.Add<CosmosFhirDataStore>()
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

            services.Add<CollectionInitializer>(sp =>
                {
                    var config = sp.GetService<CosmosDataStoreConfiguration>();
                    var upgradeManager = sp.GetService<FhirCollectionUpgradeManager>();
                    var loggerFactory = sp.GetService<ILoggerFactory>();
                    var namedCosmosCollectionConfiguration = sp.GetService<IOptionsMonitor<CosmosCollectionConfiguration>>();
                    var cosmosCollectionConfiguration = namedCosmosCollectionConfiguration.Get(Constants.CollectionConfigurationName);

                    return new CollectionInitializer(
                        cosmosCollectionConfiguration.CollectionId,
                        config,
                        cosmosCollectionConfiguration.InitialCollectionThroughput,
                        upgradeManager,
                        loggerFactory.CreateLogger<CollectionInitializer>());
                })
                .Singleton()
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

            services.Add<CosmosFhirOperationDataStore>()
                .Scoped()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<FhirDocumentClientInitializer>()
                .Singleton()
                .AsService<IDocumentClientInitializer>();

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

        private static IFhirServerBuilder AddCosmosDbHealthCheck(this IFhirServerBuilder fhirServerBuilder)
        {
            fhirServerBuilder.Services.AddHealthChecks()
                .AddCheck<FhirCosmosHealthCheck>(name: nameof(FhirCosmosHealthCheck));

            return fhirServerBuilder;
        }
    }
}
