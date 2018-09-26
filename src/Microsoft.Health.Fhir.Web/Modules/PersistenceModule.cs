// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Azure.Documents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Features.Container;
using Microsoft.Health.Fhir.CosmosDb.Configs;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Versioning;
using Microsoft.Health.Fhir.Web.Features.Storage;

namespace Microsoft.Health.Fhir.Web.Modules
{
    public class PersistenceModule : IStartupModule
    {
        public void Load(IServiceCollection services)
        {
            EnsureArg.IsNotNull(services);

            services.Add(provider =>
                {
                    var config = new CosmosDataStoreConfiguration();
                    provider.GetService<IConfiguration>().GetSection("DataStore").Bind(config);

                    if (string.IsNullOrEmpty(config.Host))
                    {
                        config.Host = CosmosDbLocalEmulator.Host;
                        config.Key = CosmosDbLocalEmulator.Key;
                    }

                    return config;
                })
                .Singleton()
                .AsSelf();

            services.Add<CosmosDataStore>()
                .Scoped()
                .AsSelf()
                .AsImplementedInterfaces();

            services.Add<DocumentClientProvider>()
                .Singleton()
                .AsSelf()
                .AsService<IStartable>() // so that it starts intializing ASAP
                .AsService<IRequireInitializationOnFirstRequest>(); // so that web requests block on its initialization.

            services.Add<DocumentClientReadWriteTestProvider>()
                .Singleton()
                .AsService<IDocumentClientTestProvider>();

            // Register IDocumentClient
            // We are intentionally not registering IDocumentClient directly, because
            // we want this codebase to support different configurations, where the
            // lifetime of the document clients can be managed outside of the IoC
            // container, which will automatically dispose it if exposed as a scoped
            // service or as transient but consumed from another scoped service.

            services.Add(sp => sp.GetService<DocumentClientProvider>().CreateDocumentClientScope())
                .Transient()
                .AsSelf()
                .AsFactory();

            services.Add<DocumentClientInitializer>()
                .Singleton()
                .AsService<IDocumentClientInitializer>();

            services.Add<CosmosDocumentQueryFactory>()
                .Singleton()
                .AsService<ICosmosDocumentQueryFactory>();

            services.Add<CosmosDocumentQueryLogger>()
                .Singleton()
                .AsService<ICosmosDocumentQueryLogger>();

            services.Add<CollectionUpgradeManager>()
                .Singleton()
                .AsService<IUpgradeManager>();

            services.TypesInSameAssemblyAs<ICollectionUpdater>()
                .AssignableTo<ICollectionUpdater>()
                .Singleton()
                .AsSelf()
                .AsService<ICollectionUpdater>();

            services.Add<CosmosDbDistributedLockFactory>()
                .Singleton()
                .AsService<ICosmosDbDistributedLockFactory>();
        }
    }
}
