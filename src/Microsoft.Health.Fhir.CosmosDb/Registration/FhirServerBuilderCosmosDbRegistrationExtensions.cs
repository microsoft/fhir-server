// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.CosmosDb;
using Microsoft.Health.Fhir.CosmosDb.Configs;
using Microsoft.Health.Fhir.CosmosDb.Features.Health;
using Microsoft.Health.Fhir.CosmosDb.Features.Queries;
using Microsoft.Health.Fhir.CosmosDb.Features.Search;
using Microsoft.Health.Fhir.CosmosDb.Features.Search.Queries;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Operations;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Registry;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.StoredProcedures;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Versioning;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection
{
    public static class FhirServerBuilderCosmosDbRegistrationExtensions
    {
        /// <summary>
        /// Adds Cosmos Db as the data store for the FHIR server.
        /// </summary>
        /// <param name="fhirServerBuilder">The FHIR server builder.</param>
        /// <returns>The builder.</returns>
        public static IFhirServerBuilder AddCosmosDb(this IFhirServerBuilder fhirServerBuilder, Action<CosmosDataStoreConfiguration> configureAction = null)
        {
            EnsureArg.IsNotNull(fhirServerBuilder, nameof(fhirServerBuilder));

            return fhirServerBuilder
                .AddCosmosDbPersistence(configureAction)
                .AddCosmosDbSearch()
                .AddCosmosDbHealthCheck();
        }

        private static IFhirServerBuilder AddCosmosDbPersistence(this IFhirServerBuilder fhirServerBuilder, Action<CosmosDataStoreConfiguration> configureAction = null)
        {
            IServiceCollection services = fhirServerBuilder.Services;

            if (services.Any(x => x.ImplementationType == typeof(CosmosContainerProvider)))
            {
                return fhirServerBuilder;
            }

            services.Add(provider =>
                {
                    var config = new CosmosDataStoreConfiguration();
                    provider.GetService<IConfiguration>().GetSection("CosmosDb").Bind(config);
                    configureAction?.Invoke(config);

                    if (string.IsNullOrEmpty(config.Host))
                    {
                        ILogger<CosmosDataStoreConfiguration> logger = provider.GetService<ILogger<CosmosDataStoreConfiguration>>();
                        logger.LogWarning("No connection string provided, attempting to connect to local emulator.");

                        config.Host = CosmosDbLocalEmulator.Host;
                        config.Key = CosmosDbLocalEmulator.Key;
                    }

                    return config;
                })
                .Singleton()
                .AsSelf();

            services.Add<CosmosContainerProvider>()
                .Singleton()
                .AsSelf()
                .AsService<IStartable>() // so that it starts initializing ASAP
                .AsService<IRequireInitializationOnFirstRequest>(); // so that web requests block on its initialization.

            services.Add<CosmosClientReadWriteTestProvider>()
                .Singleton()
                .AsService<ICosmosClientTestProvider>();

            // Register Container
            // We are intentionally not registering Container directly, because
            // we want this codebase to support different configurations, where the
            // lifetime of the document clients can be managed outside of the IoC
            // container, which will automatically dispose it if exposed as a scoped
            // service or as transient but consumed from another scoped service.

            services.Add<IScoped<Container>>(sp => sp.GetService<CosmosContainerProvider>().CreateContainerScope())
                .Transient()
                .AsSelf()
                .AsFactory();

            services.Add<CosmosQueryFactory>()
                .Singleton()
                .AsService<ICosmosQueryFactory>();

            services.Add<CosmosDbDistributedLockFactory>()
                .Singleton()
                .AsService<ICosmosDbDistributedLockFactory>();

            services.Add<RetryExceptionPolicyFactory>()
                .Singleton()
                .AsSelf();

            services.AddTransient<IConfigureOptions<CosmosCollectionConfiguration>>(
                provider => new ConfigureNamedOptions<CosmosCollectionConfiguration>(
                    Constants.CollectionConfigurationName,
                    cosmosCollectionConfiguration =>
                    {
                        var configuration = provider.GetRequiredService<IConfiguration>();
                        configuration.GetSection("FhirServer:CosmosDb").Bind(cosmosCollectionConfiguration);
                        if (string.IsNullOrWhiteSpace(cosmosCollectionConfiguration.CollectionId))
                        {
                            IModelInfoProvider modelInfoProvider = provider.GetRequiredService<IModelInfoProvider>();
                            cosmosCollectionConfiguration.CollectionId = modelInfoProvider.Version == FhirSpecification.Stu3 ? "fhir" : $"fhir{modelInfoProvider.Version}";
                        }
                    }));

            services.Add<CosmosFhirDataStore>()
                .Scoped()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<CosmosTransactionHandler>()
                .Scoped()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<CollectionUpgradeManager>()
                .Singleton()
                .AsSelf()
                .AsService<IUpgradeManager>();

            services.Add<CosmosQueryLogger>()
                .Singleton()
                .AsService<ICosmosQueryLogger>();

            services.Add<CollectionInitializer>(sp =>
                {
                    var config = sp.GetService<CosmosDataStoreConfiguration>();
                    var upgradeManager = sp.GetService<CollectionUpgradeManager>();
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
                .Transient()
                .AsService<ICollectionUpdater>();

            services.Add<StoredProcedureInstaller>()
                .Transient()
                .AsService<ICollectionUpdater>();

            services.Add<CosmosDbStatusRegistryInitializer>()
                .Transient()
                .AsService<ICollectionUpdater>();

            services.TypesInSameAssemblyAs<IStoredProcedure>()
                .AssignableTo<IStoredProcedure>()
                .Singleton()
                .AsSelf()
                .AsService<IStoredProcedure>();

            services.Add<CosmosFhirOperationDataStore>()
                .Scoped()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<FhirCosmosClientInitializer>()
                .Singleton()
                .AsService<ICosmosClientInitializer>();

            services.Add<CosmosResponseProcessor>()
                .Singleton()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<CosmosDbStatusRegistry>()
                .Singleton()
                .AsSelf()
                .ReplaceService<ISearchParameterRegistry>();

            services.TypesInSameAssemblyAs<FhirCosmosClientInitializer>()
                .AssignableTo<RequestHandler>()
                .Singleton()
                .AsService<RequestHandler>();

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
                .AddCheck<CosmosHealthCheck>(name: nameof(CosmosHealthCheck));

            return fhirServerBuilder;
        }
    }
}
