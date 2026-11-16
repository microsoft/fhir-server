// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.FhirPath
{
    /// <summary>
    /// Bounded cache of compiled FHIRPath expressions shared by the FHIRPath providers.
    /// </summary>
    /// <remarks>
    /// This type is thread-safe. A hit is a lock-free dictionary lookup that returns the same compiled instance
    /// every time; providers are called many times per indexed resource, so hits must not take locks or allocate.
    /// A miss compiles under a single mutation lock, so each expression is compiled at most once while it is cached.
    /// When an addition exceeds the capacity, the oldest entry is evicted (first in, first out). Compile failures
    /// are not cached; they propagate to the caller.
    /// </remarks>
    internal sealed class CompiledFhirPathCache
    {
        /// <summary>
        /// The number of compiled expressions each provider keeps.
        /// </summary>
        internal const int DefaultCapacity = 4096;

        private readonly ConcurrentDictionary<string, ICompiledFhirPath> _cache = new(StringComparer.Ordinal);
        private readonly Queue<string> _insertionOrder = new();
        private readonly object _mutationSync = new();
        private readonly int _capacity;
        private readonly Func<string, ICompiledFhirPath> _compile;

        /// <summary>
        /// Initializes a new instance of the <see cref="CompiledFhirPathCache"/> class.
        /// </summary>
        /// <param name="capacity">The maximum number of cached expressions.</param>
        /// <param name="compile">Compiles an expression that is not cached. It is called under the cache's mutation lock.</param>
        public CompiledFhirPathCache(int capacity, Func<string, ICompiledFhirPath> compile)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
            ArgumentNullException.ThrowIfNull(compile);

            _capacity = capacity;
            _compile = compile;
        }

        /// <summary>
        /// Returns the cached compiled expression, compiling and caching it on a miss.
        /// </summary>
        /// <param name="expression">The FHIRPath expression.</param>
        /// <returns>The compiled expression.</returns>
        public ICompiledFhirPath GetOrAdd(string expression)
        {
            if (_cache.TryGetValue(expression, out ICompiledFhirPath compiled))
            {
                return compiled;
            }

            lock (_mutationSync)
            {
                if (_cache.TryGetValue(expression, out compiled))
                {
                    return compiled;
                }

                compiled = _compile(expression);
                if (!_cache.TryAdd(expression, compiled))
                {
                    throw new InvalidOperationException("The compiled FHIRPath cache changed while holding its mutation lock.");
                }

                _insertionOrder.Enqueue(expression);

                // Bound on the queue's count: ConcurrentDictionary.Count acquires every lock stripe.
                if (_insertionOrder.Count > _capacity)
                {
                    string oldestExpression = _insertionOrder.Dequeue();
                    if (!_cache.TryRemove(oldestExpression, out _))
                    {
                        throw new InvalidOperationException("The compiled FHIRPath cache and its eviction queue are inconsistent.");
                    }
                }

                return compiled;
            }
        }
    }
}
