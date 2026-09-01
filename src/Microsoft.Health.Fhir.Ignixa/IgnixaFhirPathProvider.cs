// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.FhirPath;

namespace Microsoft.Health.Fhir.Ignixa
{
    /// <summary>
    /// Compiles expressions with the Ignixa FHIRPath engine.
    /// </summary>
    public sealed class IgnixaFhirPathProvider : IFhirPathProvider
    {
        private const int CacheSize = 4096;
        private readonly ConcurrentDictionary<string, ICompiledFhirPath> _cache = new(StringComparer.Ordinal);
        private readonly Queue<string> _insertionOrder = new();
        private readonly object _cacheMutationSync = new();
        private readonly IgnixaEvaluationContextBridge _contextBridge;

        /// <summary>
        /// Initializes a new instance of the <see cref="IgnixaFhirPathProvider"/> class.
        /// </summary>
        /// <param name="schemaContext">The active FHIR schema.</param>
        public IgnixaFhirPathProvider(IgnixaSchemaContext schemaContext)
        {
            _contextBridge = new IgnixaEvaluationContextBridge(schemaContext);
        }

        /// <inheritdoc />
        public ICompiledFhirPath Compile(string expression)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(expression);

            if (_cache.TryGetValue(expression, out ICompiledFhirPath compiled))
            {
                return compiled;
            }

            lock (_cacheMutationSync)
            {
                if (_cache.TryGetValue(expression, out compiled))
                {
                    return compiled;
                }

                compiled = new IgnixaCompiledFhirPath(expression, _contextBridge);
                if (!_cache.TryAdd(expression, compiled))
                {
                    throw new InvalidOperationException("The compiled FHIRPath cache changed while holding its mutation lock.");
                }

                _insertionOrder.Enqueue(expression);

                if (_insertionOrder.Count > CacheSize)
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
