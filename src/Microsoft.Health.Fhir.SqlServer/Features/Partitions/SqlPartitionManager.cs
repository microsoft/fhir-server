// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Features.Partitions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Partitions
{
    /// <summary>Maintains an immutable startup snapshot of SQL partition settings.</summary>
    public sealed class SqlPartitionManager : IPartitionManager
    {
        private readonly DatabasePartitioningOptions _options;
        private readonly IPartitionInfo[] _partitions;

        /// <summary>Initializes the partition registry.</summary>
        public SqlPartitionManager(IOptions<DatabasePartitioningOptions> options)
        {
            _options = EnsureArg.IsNotNull(options?.Value, nameof(options));
            _partitions = _options.Partitions.Select(p => new SqlPartitionInfo { Database = p.Value.Database, Id = p.Key }).ToArray<IPartitionInfo>();
        }

        /// <inheritdoc />
        public bool IsEnabled => _options.Partitions.Count > 0;

        /// <inheritdoc />
        public IReadOnlyCollection<IPartitionInfo> GetPartitions() => _partitions;

        /// <inheritdoc />
        public bool TryGetPartition(string partitionId, out IPartitionInfo partitionInfo)
        {
            if (_options.Partitions.TryGetValue(partitionId, out PartitionOptions partition))
            {
                partitionInfo = new SqlPartitionInfo { Database = partition.Database, Id = partitionId };
                return true;
            }

            partitionInfo = default;
            return false;
        }
    }
}
