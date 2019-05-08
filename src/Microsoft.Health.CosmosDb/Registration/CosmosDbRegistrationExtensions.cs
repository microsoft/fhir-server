// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using EnsureThat;
using Microsoft.Azure.Documents;
using Microsoft.Extensions.Configuration;
using Microsoft.Health.CosmosDb.Configs;
using Microsoft.Health.CosmosDb.Features.Storage;
using Microsoft.Health.Extensions.DependencyInjection;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class CosmosDbRegistrationExtensions
    {
        /// <summary>
        /// Add common CosmosDb services
        /// Settings are read from the "CosmosDB" configuration section and can optionally be overridden with the <paramref name="configureAction"/> delegate.
        /// </summary>
        /// <param name="services">The IServiceCollection</param>
        /// <param name="configureAction">An optional delegate for overriding configuration properties.</param>
        /// <returns>The service collection</returns>
        public static IServiceCollection AddCosmosDb(this IServiceCollection services, Action<CosmosDataStoreConfiguration> configureAction = null)
        {
            EnsureArg.IsNotNull(services, nameof(services));

            if (services.Any(x => x.ImplementationType == typeof(DocumentClientProvider)))
            {
                return services;
            }

            services.Add(provider =>
                {
                    var config = new CosmosDataStoreConfiguration();
                    provider.GetService<IConfiguration>().GetSection("CosmosDb").Bind(config);
                    configureAction?.Invoke(config);

                    if (string.IsNullOrEmpty(config.Host))
                    {
                        config.Host = CosmosDbLocalEmulator.Host;
                        config.Key = CosmosDbLocalEmulator.Key;
                    }

                    return config;
                })
                .Singleton()
                .AsSelf();

            services.Add<DocumentClientProvider>()
                .Singleton()
                .AsSelf()
                .AsService<IStartable>() // so that it starts initializing ASAP
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

            services.Add<IScoped<IDocumentClient>>(sp => sp.GetService<DocumentClientProvider>().CreateDocumentClientScope())
                .Transient()
                .AsSelf()
                .AsFactory();

            services.Add<CosmosDocumentQueryFactory>()
                .Singleton()
                .AsService<ICosmosDocumentQueryFactory>();

            services.Add<CosmosDbDistributedLockFactory>()
                .Singleton()
                .AsService<ICosmosDbDistributedLockFactory>();

            services.Add<RetryExceptionPolicyFactory>()
                .Singleton()
                .AsSelf();

            return services;
        }
    }
}
