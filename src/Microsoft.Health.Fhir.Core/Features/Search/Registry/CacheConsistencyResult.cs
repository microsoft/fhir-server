// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Search.Registry
{
    /// <summary>
    /// Result of checking whether all active instances have converged their search parameter caches
    /// to a target version.
    /// </summary>
    public class CacheConsistencyResult
    {
        /// <summary>
        /// Gets or sets a value indicating whether all active instances have consistent caches.
        /// </summary>
        public bool IsConsistent { get; set; }

        /// <summary>
        /// Gets or sets the number of active hosts discovered.
        /// </summary>
        public int ActiveHosts { get; set; }

        /// <summary>
        /// Gets or sets the number of hosts that have converged to the target version.
        /// </summary>
        public int ConvergedHosts { get; set; }
    }
}
