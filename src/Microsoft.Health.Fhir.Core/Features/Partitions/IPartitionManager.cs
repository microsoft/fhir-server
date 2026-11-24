// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Partitions
{
    /// <summary>
    /// Represents a manager of logical groups of FHIR data.
    /// </summary>
    public interface IPartitionManager
    {
        /// <summary>
        /// Gets a flag indicating whether the partitioning is enabled.
        /// </summary>
        /// <value><see langword="true"/> if enabled; otherwise, <see langword="false"/>.</value>
        bool IsEnabled { get; }

        /// <summary>
        /// Attempts to get the partition associated with the specified identifier.
        /// </summary>
        /// <param name="partitionId">The identifier of the partition to get.</param>
        /// <param name="partitionInfo">
        /// When this method returns, contains the partition associated with the specified identifier, if the
        /// partition is found; otherwise, <see langword="null"/>. This parameter is passed uninitialized.
        /// </param>
        /// <returns>
        /// <see langword="true"/> if the partition was found; otherwise, <see langword="false"/>.
        /// </returns>
        bool TryGetPartition(string partitionId, out IPartitionInfo partitionInfo);
    }
}
