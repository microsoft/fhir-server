// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Messages.Stats
{
    /// <summary>
    /// Response containing resource statistics.
    /// </summary>
    public class StatsResponse
    {
        private readonly Dictionary<string, ResourceTypeStats> _resourceStats = new();

        /// <summary>
        /// Resource type statistics: key is resource type, value is ResourceTypeStats.
        /// </summary>
        public IDictionary<string, ResourceTypeStats> ResourceStats => _resourceStats;
    }
}
