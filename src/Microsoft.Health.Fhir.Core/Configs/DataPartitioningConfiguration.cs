// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Configs
{
    /// <summary>
    /// Configuration settings for data partitioning feature.
    /// </summary>
    public class DataPartitioningConfiguration
    {
        /// <summary>
        /// Gets or sets a value indicating whether data partitioning is enabled.
        /// Once enabled with data in multiple partitions, this cannot be disabled.
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Gets or sets the default partition name for non-partitioned requests.
        /// </summary>
        public string DefaultPartitionName { get; set; } = "default";

        /// <summary>
        /// Gets or sets the system partition name for system resources.
        /// </summary>
        public string SystemPartitionName { get; set; } = "system";
    }
}
