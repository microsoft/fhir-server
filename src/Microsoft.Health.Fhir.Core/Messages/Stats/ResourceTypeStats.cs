// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Messages.Stats
{
    /// <summary>
    /// Statistics for a single resource type.
    /// </summary>
    public class ResourceTypeStats
    {
        /// <summary>
        /// Total number of resources of this type.
        /// </summary>
        public long TotalCount { get; set; }

        /// <summary>
        /// Number of resources of this type that are not historical or deleted.
        /// </summary>
        public long ActiveCount { get; set; }
    }
}
