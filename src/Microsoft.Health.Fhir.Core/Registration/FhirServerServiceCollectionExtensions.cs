// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Threading;

namespace Microsoft.Health.Fhir.Core.Registration
{
    /// <summary>
    /// Extension methods for configuring threading services.
    /// </summary>
    public static partial class FhirServerServiceCollectionExtensions
    {
        /// <summary>
        /// Adds adaptive threading services to the service collection.
        /// </summary>
        /// <param name="services">The service collection.</param>
        public static IServiceCollection AddDynamicThreading(this IServiceCollection services)
        {
            services.AddSingleton<IRuntimeResourceMonitor, RuntimeResourceMonitor>();
            services.AddSingleton<IDynamicThreadingService, DynamicThreadingService>();
            services.AddSingleton<IResourceThrottlingService, ResourceThrottlingService>();
            services.AddSingleton<IDynamicThreadPoolManager, DynamicThreadPoolManager>();

            return services;
        }
    }
}
