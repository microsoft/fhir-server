// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Partitions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Partitions
{
    /// <summary>
    /// SQL helpers for <see cref="IPartitionInfo"/>.
    /// </summary>
    public static class PartitionInfoExtensions
    {
        /// <summary>
        /// Gets the partition's database name.
        /// </summary>
        /// <param name="partition">The partition, or <see langword="null"/> when partitioning is not in use.</param>
        /// <returns>The database name, or <see langword="null"/> for the database in the connection string.</returns>
        /// <exception cref="InvalidOperationException">The partition is not a <see cref="SqlPartitionInfo"/>.</exception>
        public static string GetDatabase(this IPartitionInfo partition) => partition switch
        {
            null => null,
            SqlPartitionInfo sqlPartition => sqlPartition.Database,
            _ => throw new InvalidOperationException("Partition is not of type SqlPartitionInfo."),
        };
    }
}
