// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Partitions
{
    /// <summary>
    /// Describes a configured partition without allowing its settings to change at runtime.
    /// </summary>
    public sealed class PartitionInfo
    {
        /// <summary>
        /// Gets the case-sensitive partition identifier used as a URL segment.
        /// </summary>
        public string Id { get; init; }

        /// <summary>
        /// Gets the partition's database name.
        /// </summary>
        public string DatabaseName { get; init; }
    }
}
