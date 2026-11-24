// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Caching
{
    /// <summary>
    /// A cache that <see cref="CacheInitializer"/> loads at startup.
    /// </summary>
    /// <param name="Name">The cache name used in logs.</param>
    /// <param name="IsPartitioned">
    /// Whether the cache is loaded once per partition, through <see cref="ScopedCache{T}"/> in a scope whose request
    /// context holds that partition, rather than once per app through <see cref="GlobalCache{T}"/>.
    /// </param>
    /// <param name="LoadAsync">Loads the cache from the given service provider.</param>
    internal sealed record CachePreload(string Name, bool IsPartitioned, Func<IServiceProvider, CancellationToken, Task> LoadAsync);
}
