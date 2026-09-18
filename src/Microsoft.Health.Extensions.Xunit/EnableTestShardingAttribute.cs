// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Extensions.Xunit
{
    /// <summary>
    /// Opts a test assembly in to environment-variable driven test sharding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sharding is only ever considered for assemblies decorated with this attribute. Without it, the
    /// sharding environment variables are ignored entirely. This matters because environment variables are
    /// typically set for a whole CI job, and a job commonly runs several test assemblies; only the assemblies
    /// that have been reviewed for shard safety should be partitioned.
    /// </para>
    /// <para>
    /// Sharding partitions whole test methods, so every theory row and every fixture argument set variant of a
    /// given method always lands in the same shard. Shards are only safe when each shard process has exclusive
    /// use of the backing service/database it talks to; see the sharding documentation for the full contract.
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
    public sealed class EnableTestShardingAttribute : Attribute
    {
    }
}
