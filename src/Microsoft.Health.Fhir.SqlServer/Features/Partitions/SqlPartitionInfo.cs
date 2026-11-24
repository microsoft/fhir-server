// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Partitions;

namespace Microsoft.Health.Fhir.SqlServer.Features.Partitions
{
    /// <summary>A SQL partition and its database.</summary>
    public sealed class SqlPartitionInfo : PartitionOptions, IPartitionInfo
    {
        /// <summary>Gets the case-sensitive partition identifier.</summary>
        public string Id { get; init; }
    }
}
