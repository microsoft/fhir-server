// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Partitions;

namespace Microsoft.Health.Fhir.Core.Features.Caching
{
    /// <summary>
    /// A registered partition cache that <see cref="CacheInitializer"/> loads at startup.
    /// </summary>
    /// <param name="Name">The cache name used in logs.</param>
    /// <param name="LoadAsync">Loads the cache for one partition.</param>
    internal sealed record PartitionCachePreload(string Name, Func<IServiceProvider, IPartitionInfo, CancellationToken, Task> LoadAsync);
}
