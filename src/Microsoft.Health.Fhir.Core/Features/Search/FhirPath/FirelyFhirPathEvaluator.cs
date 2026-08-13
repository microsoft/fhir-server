// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.FhirPath;

namespace Microsoft.Health.Fhir.Core.Features.Search.FhirPath
{
    /// <summary>
    /// The default <see cref="IFhirPathEvaluator"/>, backed by the Firely SDK's FHIRPath engine.
    /// </summary>
    /// <remarks>
    /// The compiler is static because Firely's <see cref="FhirPathCompiler"/> resolves against
    /// <see cref="FhirPathCompiler.DefaultSymbolTable"/>, which the API layer populates once at startup with
    /// the FHIR extension functions (<c>resolve()</c>, <c>ofType()</c>, <c>hasValue()</c>, ...).
    /// </remarks>
    public sealed class FirelyFhirPathEvaluator : IFhirPathEvaluator
    {
        private static readonly FhirPathCompiler Compiler = new();

        private readonly ConcurrentDictionary<string, ICompiledFhirPath> _cache = new(StringComparer.Ordinal);

        /// <inheritdoc />
        public ICompiledFhirPath Compile(string expression)
        {
            EnsureArg.IsNotNullOrWhiteSpace(expression, nameof(expression));

            return _cache.GetOrAdd(expression, static e => new FirelyCompiledFhirPath(e, Compiler.Compile(e)));
        }

        private sealed class FirelyCompiledFhirPath : ICompiledFhirPath
        {
            private readonly CompiledExpression _compiled;

            public FirelyCompiledFhirPath(string expression, CompiledExpression compiled)
            {
                Expression = expression;
                _compiled = compiled;
            }

            public string Expression { get; }

            public IEnumerable<ITypedElement> Evaluate(ITypedElement input, EvaluationContext context)
            {
                EnsureArg.IsNotNull(input, nameof(input));

                return _compiled.Invoke(input, context);
            }
        }
    }
}
