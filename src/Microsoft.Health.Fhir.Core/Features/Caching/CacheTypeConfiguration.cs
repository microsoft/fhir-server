// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Features.Caching
{
    /// <summary>
    /// Configuration for a specific cache type
    /// </summary>
    public class CacheTypeConfiguration
    {
        /// <summary>
        /// How long cache entries should live
        /// </summary>
        public TimeSpan CacheExpiry { get; set; } = TimeSpan.FromHours(1);

        /// <summary>
        /// Key prefix for all cache keys of this type
        /// </summary>
        public string KeyPrefix { get; set; }

        /// <summary>
        /// Whether to enable versioning for this cache type
        /// </summary>
        public bool EnableVersioning { get; set; } = true;

        /// <summary>
        /// Whether to enable compression for this cache type
        /// </summary>
        public bool EnableCompression { get; set; } = true;
    }
}
