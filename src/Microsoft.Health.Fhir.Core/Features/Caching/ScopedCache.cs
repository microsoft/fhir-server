// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;

namespace Microsoft.Health.Fhir.Core.Features.Caching
{
    /// <summary>
    /// Gets a <see cref="PartitionCache{T}"/> value for the current request's partition.
    /// </summary>
    /// <remarks>
    /// Registered as scoped so it is only used where a request context exists. Code without a request
    /// (refresh timers, jobs) calls <see cref="PartitionCache{T}"/> with a partition ID, or sets a background
    /// request context with the partition first.
    /// </remarks>
    /// <typeparam name="T">The cached value type.</typeparam>
    public sealed class ScopedCache<T>
    {
        private readonly PartitionCache<T> _cache;
        private readonly RequestContextAccessor<IFhirRequestContext> _requestContextAccessor;

        /// <summary>
        /// Initializes a new instance of the <see cref="ScopedCache{T}"/> class.
        /// </summary>
        /// <param name="cache">The partition cache.</param>
        /// <param name="requestContextAccessor">The accessor for the current request context.</param>
        public ScopedCache(PartitionCache<T> cache, RequestContextAccessor<IFhirRequestContext> requestContextAccessor)
        {
            _cache = EnsureArg.IsNotNull(cache, nameof(cache));
            _requestContextAccessor = EnsureArg.IsNotNull(requestContextAccessor, nameof(requestContextAccessor));
        }

        /// <summary>
        /// Gets the value for the current partition.
        /// </summary>
        /// <returns>The cached value.</returns>
        public T Get() => _cache.Get(_requestContextAccessor.RequestContext?.Partition?.Id);

        /// <summary>
        /// Gets the value for the current partition.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The cached value.</returns>
        public ValueTask<T> GetAsync(CancellationToken cancellationToken = default) =>
            _cache.GetAsync(_requestContextAccessor.RequestContext?.Partition?.Id, cancellationToken);
    }
}
