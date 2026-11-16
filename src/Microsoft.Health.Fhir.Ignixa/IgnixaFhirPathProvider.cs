// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.FhirPath;

namespace Microsoft.Health.Fhir.Ignixa
{
    /// <summary>
    /// Compiles expressions with the Ignixa FHIRPath engine.
    /// </summary>
    public sealed class IgnixaFhirPathProvider : IFhirPathProvider
    {
        private readonly CompiledFhirPathCache _cache;

        /// <summary>
        /// Initializes a new instance of the <see cref="IgnixaFhirPathProvider"/> class.
        /// </summary>
        /// <param name="schemaContext">The active FHIR schema.</param>
        public IgnixaFhirPathProvider(IgnixaSchemaContext schemaContext)
        {
            var contextBridge = new IgnixaEvaluationContextBridge(schemaContext);
            _cache = new CompiledFhirPathCache(
                CompiledFhirPathCache.DefaultCapacity,
                expression => new IgnixaCompiledFhirPath(expression, contextBridge));
        }

        /// <inheritdoc />
        public ICompiledFhirPath Compile(string expression)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(expression);
            return _cache.GetOrAdd(expression);
        }
    }
}
