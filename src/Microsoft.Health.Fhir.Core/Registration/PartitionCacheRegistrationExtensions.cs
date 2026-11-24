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
    /// Registers partition caches.
    /// </summary>
    public static class PartitionCacheRegistrationExtensions
    {
        /// <summary>
        /// Registers <typeparamref name="TCache"/> as a singleton, available as itself, as <see cref="PartitionCache{T}"/>
        /// and through <see cref="ScopedCache{T}"/>, and preloads it for every partition at startup.
        /// </summary>
        /// <remarks>
        /// The preloading hosted service is added on the first call. Hosted services start in registration order,
        /// so call this after registering anything the cache needs at startup (e.g. schema initialization).
        /// </remarks>
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

            services.AddSingleton(new PartitionCachePreload(
                typeof(TCache).Name,
                async (sp, partition, cancellationToken) => await sp.GetRequiredService<PartitionCache<T>>().GetAsync(partition, cancellationToken)));
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, CacheInitializer>());

            return services;
        }
    }
}
