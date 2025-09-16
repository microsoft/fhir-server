// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    /// <summary>
    /// Extension methods to configure Redis distributed caching for startup
    /// </summary>
    public static class RedisServiceExtensions
    {
        /// <summary>
        /// Configures Redis distributed caching if enabled in configuration.
        /// Only registers Redis services when both Enabled=true and ConnectionString is provided.
        /// Includes graceful degradation settings for production reliability.
        /// </summary>
        /// <param name="services">The service collection</param>
        /// <param name="configuration">The configuration root</param>
        /// <returns>The service collection for method chaining</returns>
        public static IServiceCollection AddRedisIfConfigured(this IServiceCollection services, IConfiguration configuration)
        {
            // Get Redis configuration
            var redisConfig = new RedisConfiguration();
            configuration.GetSection("FhirServer:Caching:Redis").Bind(redisConfig);

            // Only configure Redis if it's enabled and has a connection string
            if (redisConfig.Enabled && !string.IsNullOrWhiteSpace(redisConfig.ConnectionString))
            {
                services.AddStackExchangeRedisCache(options =>
                {
                    options.Configuration = redisConfig.ConnectionString;
                    options.InstanceName = "FhirServer";
                    options.ConfigurationOptions.AbortOnConnectFail = false; // Allow graceful degradation
                    options.ConfigurationOptions.ConnectTimeout = (int)redisConfig.OperationTimeout.TotalMilliseconds;
                    options.ConfigurationOptions.SyncTimeout = (int)redisConfig.OperationTimeout.TotalMilliseconds;
                });
            }

            return services;
        }
    }
}
