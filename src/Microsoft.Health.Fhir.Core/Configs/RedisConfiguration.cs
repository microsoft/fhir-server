// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class RedisConfiguration
    {
        public const string SectionName = "Redis";

        public bool Enabled { get; set; } = false;

        /// <summary>
        /// When true, the application will use the managed identity of the Azure resource to authenticate to Redis.
        /// Host property is required when this is true.
        /// </summary>
        public bool UseManagedIdentity { get; set; } = true;

        /// <summary>
        /// The Redis host endpoint (e.g., myredis.redis.cache.windows.net).
        /// Required when UseManagedIdentity is true.
        /// </summary>
        public string Host { get; set; } = string.Empty;

        /// <summary>
        /// The Redis port. Defaults to 6380 for SSL connections with Azure Redis Cache.
        /// </summary>
        public int Port { get; set; } = 6380;

        /// <summary>
        /// Whether to use SSL connection. Defaults to true for Azure Redis Cache.
        /// </summary>
        public bool UseSsl { get; set; } = true;

        /// <summary>
        /// The client ID for user-assigned managed identity. Leave empty for system-assigned managed identity.
        /// Only used when UseManagedIdentity is true.
        /// </summary>
        public string ManagedIdentityClientId { get; set; } = string.Empty;

        public string InstanceName { get; set; } = string.Empty;

        /// <summary>
        /// Delay in milliseconds before processing search parameter change notifications.
        /// Used to debounce multiple notifications and reduce database calls.
        /// </summary>
        public int SearchParameterNotificationDelayMs { get; set; } = 10000; // Default 10 seconds

        public RedisNotificationChannels NotificationChannels { get; } = new RedisNotificationChannels();

        public RedisConnectionConfiguration Configuration { get; } = new RedisConnectionConfiguration();

        /// <summary>
        /// Validates the Redis configuration.
        /// </summary>
        public void Validate()
        {
            if (Enabled)
            {
                if (UseManagedIdentity)
                {
                    if (string.IsNullOrEmpty(Host))
                    {
                        throw new InvalidOperationException("Host is required when UseManagedIdentity is true.");
                    }
                }
            }
        }
    }
}
