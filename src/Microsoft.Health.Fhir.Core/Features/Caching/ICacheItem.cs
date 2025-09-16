// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Features.Caching
{
    /// <summary>
    /// Interface for cache items that have a unique key and last updated timestamp
    /// </summary>
    public interface ICacheItem
    {
        /// <summary>
        /// Unique key for this cache item
        /// </summary>
        string CacheKey { get; }

        /// <summary>
        /// Last updated timestamp for this item
        /// </summary>
        DateTimeOffset LastUpdated { get; }
    }
}
