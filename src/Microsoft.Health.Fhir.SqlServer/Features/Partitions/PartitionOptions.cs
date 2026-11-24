// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlServer.Features.Partitions
{
    /// <summary>
    /// Configures a single SQL partition.
    /// </summary>
    public class PartitionOptions
    {
        /// <summary>
        /// Gets the name of the database that stores the partition's data.
        /// </summary>
        public string Database { get; init; }
    }
}
