// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using EnsureThat;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Health.Fhir.Core.Features.Caching;

namespace Microsoft.Extensions.DependencyInjection
{
    /// <summary>
    /// Registers caches built on <see cref="GlobalCache{T}"/> and <see cref="PartitionCache{T}"/>.
    /// </summary>
    /// <remarks>
    /// Each call also adds the cache to the startup preload, which runs after all hosted services have started,
    /// so registration order doesn't matter.
    /// </remarks>
    public static class CacheRegistrationExtensions
    {
        /// <summary>
        /// Registers <typeparamref name="TCache"/> as a singleton, available as itself and as <see cref="GlobalCache{T}"/>,
        /// and preloads it once at startup.
        /// </summary>
        /// <typeparam name="TCache">The cache type.</typeparam>
        /// <typeparam name="T">The cached value type.</typeparam>
        /// <param name="services">The service collection.</param>
        /// <returns>The same service collection, for chaining.</returns>
        public static IServiceCollection AddGlobalCache<TCache, T>(this IServiceCollection services)
            where TCache : GlobalCache<T>
        {
            EnsureArg.IsNotNull(services, nameof(services));

            if (services.Any(descriptor => descriptor.ServiceType == typeof(GlobalCache<T>)))
            {
                return services;
            }

            services.TryAddSingleton<TCache>();
            services.AddSingleton<GlobalCache<T>>(sp => sp.GetRequiredService<TCache>());

            return services.AddPreload(new CachePreload(
                typeof(TCache).Name,
                IsPartitioned: false,
                async (sp, cancellationToken) => await sp.GetRequiredService<GlobalCache<T>>().GetAsync(cancellationToken)));
        }

        /// <summary>
        /// Registers <typeparamref name="TCache"/> as a singleton, available as itself, as <see cref="PartitionCache{T}"/>
        /// and through <see cref="ScopedCache{T}"/>, and preloads it for every partition at startup.
        /// </summary>
        /// <typeparam name="TCache">The cache type.</typeparam>
        /// <typeparam name="T">The cached value type.</typeparam>
        /// <param name="services">The service collection.</param>
        /// <returns>The same service collection, for chaining.</returns>
        public static IServiceCollection AddPartitionCache<TCache, T>(this IServiceCollection services)
            where TCache : PartitionCache<T>
        {
            EnsureArg.IsNotNull(services, nameof(services));

            if (services.Any(descriptor => descriptor.ServiceType == typeof(PartitionCache<T>)))
            {
                return services;
            }

            services.TryAddSingleton<TCache>();
            services.AddSingleton<PartitionCache<T>>(sp => sp.GetRequiredService<TCache>());
            services.TryAddScoped(typeof(ScopedCache<>));

            return services.AddPreload(new CachePreload(
                typeof(TCache).Name,
                IsPartitioned: true,
                async (scopedProvider, cancellationToken) => await scopedProvider.GetRequiredService<ScopedCache<T>>().GetAsync(cancellationToken)));
        }

        private static IServiceCollection AddPreload(this IServiceCollection services, CachePreload preload)
        {
            services.AddSingleton(preload);
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, CacheInitializer>());
            return services;
        }
    }
}
