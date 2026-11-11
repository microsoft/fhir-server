// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Partitions;

namespace Microsoft.Health.Fhir.Core.Configs
{
    /// <summary>
    /// Configures the partitions available when the server starts.
    /// </summary>
    public class PartitionConfiguration
    {
        /// <summary>
        /// Gets the partitions. Changes require restarting the server.
        /// </summary>
        public IList<PartitionInfo> Partitions { get; } = new List<PartitionInfo>();

        /// <summary>
        /// Validates required partition settings and unique, URL-safe partition identifiers.
        /// </summary>
        public void Validate()
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (PartitionInfo partition in Partitions)
            {
                if (partition == null || string.IsNullOrWhiteSpace(partition.Id) ||
                    partition.Id is "." or ".." || !partition.Id.All(IsUnreserved))
                {
                    throw new InvalidOperationException("FhirServer:PartitionConfiguration:Partitions requires an Id containing only ASCII letters, digits, '-', '_', '.', or '~', other than '.' or '..'.");
                }

                if (!ids.Add(partition.Id))
                {
                    throw new InvalidOperationException($"FhirServer:PartitionConfiguration:Partitions contains a duplicate Id '{partition.Id}'.");
                }

                if (string.IsNullOrWhiteSpace(partition.DatabaseName))
                {
                    throw new InvalidOperationException($"FhirServer:PartitionConfiguration:Partitions requires a DatabaseName for partition '{partition.Id}'.");
                }
            }
        }

        private static bool IsUnreserved(char value) =>
            char.IsAsciiLetterOrDigit(value) || value is '-' or '_' or '.' or '~';
    }
}
