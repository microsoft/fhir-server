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
    /// A registered cache that <see cref="CacheInitializer"/> loads at startup.
    /// </summary>
    /// <param name="Name">The cache name used in logs.</param>
    /// <param name="IsPartitioned">Whether the cache is loaded once per partition rather than once per app.</param>
    /// <param name="LoadAsync">Loads the cache; the partition is <see langword="null"/> for app-wide caches.</param>
    internal sealed record CachePreload(string Name, bool IsPartitioned, Func<IServiceProvider, IPartitionInfo, CancellationToken, Task> LoadAsync);
}
