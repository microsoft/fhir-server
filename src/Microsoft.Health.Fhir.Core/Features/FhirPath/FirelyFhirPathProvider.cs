// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.FhirPath;
using Hl7.Fhir.Utility;
using Hl7.FhirPath;

namespace Microsoft.Health.Fhir.Core.Features.FhirPath
{
    /// <summary>
    /// Compiles expressions with the Firely FHIRPath engine.
    /// </summary>
    public sealed class FirelyFhirPathProvider : IFhirPathProvider
    {
        private const int CacheSize = 4096;
        private readonly FhirPathCompilerCache _cache;

        /// <summary>
        /// Initializes a new instance of the <see cref="FirelyFhirPathProvider"/> class.
        /// </summary>
        public FirelyFhirPathProvider()
        {
            FhirPathCompiler.DefaultSymbolTable.AddFhirExtensions();
            _cache = new FhirPathCompilerCache(new FhirPathCompiler(FhirPathCompiler.DefaultSymbolTable), CacheSize);
        }

        /// <inheritdoc />
        public ICompiledFhirPath Compile(string expression)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(expression);
            return new FirelyCompiledFhirPath(expression, _cache.GetCompiledExpression(expression));
        }
    }
}
