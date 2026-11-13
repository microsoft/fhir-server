// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Frozen;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Partitions;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    /// <summary>Provides an empty registry because Cosmos does not support service partitions.</summary>
    public sealed class CosmosPartitionManager : IPartitionManager
    {
        /// <inheritdoc />
        public IReadOnlySet<string> Partitions { get; } = FrozenSet<string>.Empty;
    }
}
