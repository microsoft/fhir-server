// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Caching;

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class RedisConfiguration
    {
        /// <summary>
        /// Whether Redis distributed caching is enabled
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Redis connection string
        /// </summary>
        public string ConnectionString { get; set; }

        /// <summary>
        /// Redis database number to use
        /// </summary>
        public int Database { get; set; } = 0;

        /// <summary>
        /// Timeout for Redis operations
        /// </summary>
        public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Number of retry attempts for Redis operations
        /// </summary>
        public int RetryAttempts { get; set; } = 3;

        /// <summary>
        /// Whether to enable Redis pub/sub for cache invalidation
        /// </summary>
        public bool EnablePubSub { get; set; } = true;

        /// <summary>
        /// Channel name for cache invalidation messages
        /// </summary>
        public string InvalidationChannelName { get; set; } = "fhir:cache:invalidation";

        /// <summary>
        /// Configuration for different cache types
        /// </summary>
        public Dictionary<string, CacheTypeConfiguration> CacheTypes { get; } = new();
    }
}
