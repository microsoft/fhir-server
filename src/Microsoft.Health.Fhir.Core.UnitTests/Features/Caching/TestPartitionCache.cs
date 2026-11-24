// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Caching;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Caching
{
    /// <summary>
    /// A <see cref="PartitionCache{T}"/> whose loader is supplied by the test, and which records each load.
    /// </summary>
    /// <typeparam name="T">The cached value type.</typeparam>
    public class TestPartitionCache<T> : PartitionCache<T>
    {
        private readonly Func<string, bool, CancellationToken, ValueTask<T>> _load;

        public TestPartitionCache(
            Func<string, bool, CancellationToken, ValueTask<T>> load,
            TimeSpan? expiry = null,
            TimeProvider timeProvider = null,
            ILogger logger = null)
            : base(logger ?? NullLogger.Instance, expiry, timeProvider)
        {
            _load = load;
        }

        public ConcurrentQueue<(string PartitionId, bool Async)> Loads { get; } = new();

        public static TestPartitionCache<T> Returning(T value) => new((_, _, _) => new ValueTask<T>(value));

        protected override ValueTask<T> GetRemoteDataAsync(string partitionId, bool async, CancellationToken cancellationToken)
        {
            Loads.Enqueue((partitionId, async));
            return _load(partitionId, async, cancellationToken);
        }
    }
}
