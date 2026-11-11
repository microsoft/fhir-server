// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;

namespace Microsoft.Health.Fhir.Core.Features.Partitions
{
    /// <summary>
    /// Holds a snapshot of partition settings loaded at startup.
    /// </summary>
    public sealed class PartitionManager : IPartitionManager
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PartitionManager"/> class.
        /// </summary>
        /// <param name="configuration">Partition settings loaded at startup.</param>
        public PartitionManager(IOptions<PartitionConfiguration> configuration)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));

            Partitions = configuration.Value.Partitions.ToFrozenDictionary(partition => partition.Id, StringComparer.Ordinal);
        }

        /// <inheritdoc />
        public IReadOnlyDictionary<string, PartitionInfo> Partitions { get; }
    }
}
