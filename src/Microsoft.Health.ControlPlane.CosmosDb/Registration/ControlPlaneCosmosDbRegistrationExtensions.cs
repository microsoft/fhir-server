// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.ControlPlane.CosmosDb;
using Microsoft.Health.ControlPlane.CosmosDb.Features.Storage;
using Microsoft.Health.ControlPlane.CosmosDb.Features.Storage.StoredProcedures;
using Microsoft.Health.ControlPlane.CosmosDb.Features.Storage.Versioning;
using Microsoft.Health.ControlPlane.CosmosDb.Health;
using Microsoft.Health.CosmosDb.Configs;
using Microsoft.Health.CosmosDb.Features.Storage;
using Microsoft.Health.CosmosDb.Features.Storage.StoredProcedures;
using Microsoft.Health.CosmosDb.Features.Storage.Versioning;
using Microsoft.Health.Extensions.DependencyInjection;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class ControlPlaneCosmosDbRegistrationExtensions
    {
        public static IServiceCollection AddControlPlaneCosmosDb(this IServiceCollection services, IConfiguration configuration)
        {
            EnsureArg.IsNotNull(services, nameof(services));
            EnsureArg.IsNotNull(configuration, nameof(configuration));

            services.AddCosmosDb();

            services.Configure<CosmosCollectionConfiguration>(Constants.CollectionConfigurationName, cosmosCollectionConfiguration => configuration.GetSection("ControlPlane:CosmosDb").Bind(cosmosCollectionConfiguration));

            services.Add<CollectionInitializer>(sp =>
                {
                    var config = sp.GetService<CosmosDataStoreConfiguration>();
                    var upgradeManager = sp.GetService<ControlPlaneCollectionUpgradeManager>();
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

            services.Add<ControlPlaneDataStore>()
                .Scoped()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<ControlPlaneCollectionUpgradeManager>()
                .Singleton()
                .AsSelf()
                .AsService<IUpgradeManager>();

            services.Add<ControlPlaneStoredProcedureInstaller>()
                .Singleton()
                .AsService<IControlPlaneCollectionUpdater>();

            services.TypesInSameAssemblyAs<IControlPlaneStoredProcedure>()
                .AssignableTo<IStoredProcedure>()
                .Singleton()
                .AsSelf()
                .AsService<IControlPlaneStoredProcedure>();

            services.AddHealthChecks()
                .AddCheck<ControlPlaneCosmosHealthCheck>(name: nameof(ControlPlaneCosmosHealthCheck));

            return services;
        }
    }
}
