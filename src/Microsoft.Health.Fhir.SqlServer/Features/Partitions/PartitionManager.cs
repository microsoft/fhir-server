// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Features.Partitions;
using Microsoft.Health.Fhir.SqlServer.Registration;

namespace Microsoft.Health.Fhir.SqlServer.Features.Partitions
{
    /// <summary>Maintains an immutable startup snapshot of SQL partition settings.</summary>
    public sealed class PartitionManager : IPartitionManager
    {
        private readonly FrozenDictionary<string, string> _databases;

        /// <summary>Initializes the partition registry.</summary>
        public PartitionManager(IOptions<PartitionConfiguration> configuration)
        {
            EnsureArg.IsNotNull(configuration, nameof(configuration));
            configuration.Value.Validate();
            _databases = configuration.Value.Partitions.ToFrozenDictionary(partition => partition.Id, partition => partition.DatabaseName, StringComparer.Ordinal);
            Partitions = _databases.Keys.ToFrozenSet(StringComparer.Ordinal);
        }

        /// <inheritdoc />
        public IReadOnlySet<string> Partitions { get; }

        /// <summary>Finds the database for a configured partition.</summary>
        public bool TryGetDatabaseName(string partitionId, out string databaseName) => _databases.TryGetValue(partitionId, out databaseName);
    }
}
