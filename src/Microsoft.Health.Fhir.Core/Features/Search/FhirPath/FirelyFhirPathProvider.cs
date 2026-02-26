// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Collections.Generic;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.FhirPath;

namespace Microsoft.Health.Fhir.Core.Features.Search.FhirPath
{
    /// <summary>
    /// Firely SDK-based implementation of <see cref="IFhirPathProvider"/>.
    /// </summary>
    /// <remarks>
    /// This is the default implementation that uses the Firely SDK's FHIRPath engine.
    /// It provides backwards compatibility for existing deployments.
    /// </remarks>
    public sealed class FirelyFhirPathProvider : IFhirPathProvider
    {
        private static readonly FhirPathCompiler Compiler = new FhirPathCompiler();
        private readonly ConcurrentDictionary<string, ICompiledFhirPath> _cache;

        /// <summary>
        /// Initializes a new instance of the <see cref="FirelyFhirPathProvider"/> class.
        /// </summary>
        public FirelyFhirPathProvider()
        {
            _cache = new ConcurrentDictionary<string, ICompiledFhirPath>();
        }

        /// <inheritdoc />
        public ICompiledFhirPath Compile(string expression)
        {
            EnsureArg.IsNotNullOrWhiteSpace(expression, nameof(expression));

            return _cache.GetOrAdd(expression, expr =>
            {
                var compiled = Compiler.Compile(expr);
                return new FirelyCompiledFhirPath(expr, compiled);
            });
        }

        /// <inheritdoc />
        public IEnumerable<ITypedElement> Evaluate(ITypedElement element, string expression, EvaluationContext context = null)
        {
            EnsureArg.IsNotNull(element, nameof(element));
            EnsureArg.IsNotNullOrWhiteSpace(expression, nameof(expression));

            var compiled = Compile(expression);
            return compiled.Evaluate(element, context);
        }

        /// <inheritdoc />
        public T Scalar<T>(ITypedElement element, string expression, EvaluationContext context = null)
        {
            EnsureArg.IsNotNull(element, nameof(element));
            EnsureArg.IsNotNullOrWhiteSpace(expression, nameof(expression));

            var compiled = Compile(expression);
            return compiled.Scalar<T>(element, context);
        }

        /// <inheritdoc />
        public bool Predicate(ITypedElement element, string expression, EvaluationContext context = null)
        {
            EnsureArg.IsNotNull(element, nameof(element));
            EnsureArg.IsNotNullOrWhiteSpace(expression, nameof(expression));

            var compiled = Compile(expression);
            return compiled.Predicate(element, context);
        }
    }
}
