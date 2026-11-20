// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Partitions
{
    /// <summary>
    /// Provides the immutable startup registry of supported partitions.
    /// </summary>
    public interface IPartitionManager
    {
        /// <summary>
        /// Gets the partition by its identifier.
        /// </summary>
        bool TryGetPartition(string partitionId, out IPartitionInfo partitionInfo);

        int GetPartitionCount();
    }
}
