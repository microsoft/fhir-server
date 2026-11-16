// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.FhirPath;
using Hl7.FhirPath;

namespace Microsoft.Health.Fhir.Core.Features.FhirPath
{
    /// <summary>
    /// Compiles expressions with the Firely FHIRPath engine.
    /// </summary>
    public sealed class FirelyFhirPathProvider : IFhirPathProvider
    {
        private readonly FhirPathCompiler _compiler;
        private readonly CompiledFhirPathCache _cache;

        /// <summary>
        /// Initializes a new instance of the <see cref="FirelyFhirPathProvider"/> class.
        /// </summary>
        public FirelyFhirPathProvider()
        {
            ElementNavFhirExtensions.PrepareFhirSymbolTableFunctions();
            _compiler = new FhirPathCompiler(FhirPathCompiler.DefaultSymbolTable);
            _cache = new CompiledFhirPathCache(
                CompiledFhirPathCache.DefaultCapacity,
                expression => new FirelyCompiledFhirPath(expression, _compiler.Compile(expression)));
        }

        /// <inheritdoc />
        public ICompiledFhirPath Compile(string expression)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(expression);
            return _cache.GetOrAdd(expression);
        }
    }
}
