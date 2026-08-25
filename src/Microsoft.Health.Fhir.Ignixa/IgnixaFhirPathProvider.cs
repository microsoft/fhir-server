// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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
        private readonly Dictionary<string, ICompiledFhirPath> _cache = new(StringComparer.Ordinal);
        private readonly Queue<string> _insertionOrder = new();
        private readonly object _sync = new();
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

            lock (_sync)
            {
                if (_cache.TryGetValue(expression, out ICompiledFhirPath compiled))
                {
                    return compiled;
                }

                compiled = new IgnixaCompiledFhirPath(expression, _contextBridge);
                _cache.Add(expression, compiled);
                _insertionOrder.Enqueue(expression);

                if (_cache.Count > CacheSize)
                {
                    _cache.Remove(_insertionOrder.Dequeue());
                }

                return compiled;
            }
        }
    }
}
