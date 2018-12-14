// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.CosmosDb.Configs;
using Microsoft.Health.CosmosDb.Features.Storage;
using Microsoft.Health.Extensions.DependencyInjection;

namespace Microsoft.Health.CosmosDb.Registration
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

            services.Add<CosmosDocumentQueryFactory>()
                .Singleton()
                .AsService<ICosmosDocumentQueryFactory>();

            services.Add<DocumentClientInitializer>()
                .Singleton()
                .AsService<IDocumentClientInitializer>();

            services.Add<CosmosDbDistributedLockFactory>()
                .Singleton()
                .AsService<ICosmosDbDistributedLockFactory>();

            return services;
        }
    }
}
