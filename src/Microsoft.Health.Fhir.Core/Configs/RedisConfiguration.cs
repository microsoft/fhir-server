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

        public string ConnectionString { get; set; } = string.Empty;

        public string InstanceName { get; set; } = string.Empty;

        /// <summary>
        /// Delay in milliseconds before processing search parameter change notifications.
        /// Used to debounce multiple notifications and reduce database calls.
        /// </summary>
        public int SearchParameterNotificationDelayMs { get; set; } = 10000; // Default 10 seconds

        public RedisNotificationChannels NotificationChannels { get; } = new RedisNotificationChannels();

        public RedisConnectionConfiguration Configuration { get; } = new RedisConnectionConfiguration();
    }
}
