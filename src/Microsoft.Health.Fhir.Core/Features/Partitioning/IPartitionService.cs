// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Partitioning
{
    /// <summary>
    /// Service for managing data partitions in the FHIR server.
    /// </summary>
    public interface IPartitionService
    {
        /// <summary>
        /// Determines if data partitioning is enabled for this deployment.
        /// </summary>
        /// <returns>True if partitioning is enabled, false otherwise.</returns>
        bool IsPartitioningEnabled();

        /// <summary>
        /// Gets the partition ID for the specified partition name.
        /// </summary>
        /// <param name="partitionName">The partition name.</param>
        /// <returns>The partition ID.</returns>
        /// <exception cref="ResourceNotFoundException">Thrown if the partition does not exist.</exception>
        Task<int> GetPartitionIdAsync(string partitionName);

        /// <summary>
        /// Creates a new partition with the specified name.
        /// </summary>
        /// <param name="partitionName">The partition name.</param>
        /// <returns>The new partition ID.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the partition name is invalid or already exists.</exception>
        Task<int> CreatePartitionAsync(string partitionName);

        /// <summary>
        /// Gets the default partition name for this deployment.
        /// </summary>
        /// <returns>The default partition name.</returns>
        string GetDefaultPartitionName();

        /// <summary>
        /// Gets the system partition ID (always 1).
        /// </summary>
        /// <returns>The system partition ID.</returns>
        int GetSystemPartitionId();
    }
}
