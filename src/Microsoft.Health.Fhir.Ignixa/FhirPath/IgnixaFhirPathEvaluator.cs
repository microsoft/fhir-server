// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Ignixa.Abstractions;
using Ignixa.Extensions.FirelySdk;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Expressions;
using Ignixa.FhirPath.Parser;
using Microsoft.Health.Fhir.Core.Features.Search.FhirPath;
using FirelyEvaluationContext = Hl7.FhirPath.EvaluationContext;
using FirelyFhirEvaluationContext = Hl7.Fhir.FhirPath.FhirEvaluationContext;
using IgnixaEvaluationContext = Ignixa.FhirPath.Evaluation.EvaluationContext;
using IgnixaFhirEvaluationContext = Ignixa.FhirPath.Evaluation.FhirEvaluationContext;

namespace Microsoft.Health.Fhir.Ignixa.FhirPath
{
    /// <summary>
    /// An <see cref="IFhirPathEvaluator"/> backed by Ignixa's FHIRPath engine.
    /// </summary>
    /// <remarks>
    /// Expressions are parsed once and, where the shape allows, lowered to a compiled delegate; expressions the
    /// delegate compiler declines are evaluated by walking the parsed AST. Both forms are cached, because search
    /// indexing evaluates the same set of search parameter expressions against every resource.
    /// </remarks>
    public sealed class IgnixaFhirPathEvaluator : IFhirPathEvaluator
    {
        private readonly FhirPathParser _parser = new();
        private readonly FhirPathEvaluator _evaluator = new();
        private readonly FhirPathDelegateCompiler _delegateCompiler;
        private readonly ConcurrentDictionary<string, ICompiledFhirPath> _cache = new(StringComparer.Ordinal);

        /// <summary>
        /// Initializes a new instance of the <see cref="IgnixaFhirPathEvaluator"/> class.
        /// </summary>
        public IgnixaFhirPathEvaluator()
        {
            _delegateCompiler = new FhirPathDelegateCompiler(_evaluator);
        }

        /// <inheritdoc />
        public ICompiledFhirPath Compile(string expression)
        {
            EnsureArg.IsNotNullOrWhiteSpace(expression, nameof(expression));

            return _cache.GetOrAdd(
                expression,
                expr =>
                {
                    Expression ast = _parser.Parse(expr);
                    return new IgnixaCompiledFhirPath(expr, ast, _delegateCompiler.TryCompile(ast), _evaluator);
                });
        }

        private sealed class IgnixaCompiledFhirPath : ICompiledFhirPath
        {
            /// <summary>
            /// Memoizes the Firely-to-Ignixa evaluation context translation. The search indexer builds one context
            /// per resource and reuses it across every search parameter expression, so without this the translation
            /// (including wrapping the <c>resolve()</c> resolver) would be repeated for each of them.
            /// </summary>
            private static readonly ConditionalWeakTable<FirelyEvaluationContext, IgnixaEvaluationContext> TranslatedContexts = new();

            private readonly Expression _ast;
            private readonly Func<IElement, IgnixaEvaluationContext, IEnumerable<IElement>> _compiledDelegate;
            private readonly FhirPathEvaluator _evaluator;

            public IgnixaCompiledFhirPath(
                string expression,
                Expression ast,
                Func<IElement, IgnixaEvaluationContext, IEnumerable<IElement>> compiledDelegate,
                FhirPathEvaluator evaluator)
            {
                Expression = expression;
                _ast = ast;
                _compiledDelegate = compiledDelegate;
                _evaluator = evaluator;
            }

            public string Expression { get; }

            public IEnumerable<ITypedElement> Evaluate(ITypedElement input, FirelyEvaluationContext context)
            {
                EnsureArg.IsNotNull(input, nameof(input));

                IElement native = IgnixaElementAccessor.ToNative(input);
                IgnixaEvaluationContext ignixaContext = Translate(context, native);

                IEnumerable<IElement> results = _compiledDelegate != null
                    ? _compiledDelegate(native, ignixaContext)
                    : _evaluator.Evaluate(native, _ast, ignixaContext);

                // Materialised deliberately. Both Ignixa evaluation paths are lazy, and the caller
                // (TypedElementSearchIndexer.ExtractSearchValues) wraps only the *call* in a try/catch before
                // enumerating the result outside it. Returning a lazy sequence would let an evaluation failure
                // escape that catch and fail the whole import item, where the Firely engine yields a logged
                // warning. Forcing evaluation here keeps the failure at the point the caller guards.
                var materialised = new List<ITypedElement>();

                foreach (IElement result in results)
                {
                    materialised.Add(SystemTypedElementAdapter.Create(result));
                }

                return materialised;
            }

            private static IgnixaEvaluationContext Translate(FirelyEvaluationContext context, IElement fallbackResource)
            {
                if (context == null)
                {
                    return new IgnixaFhirEvaluationContext { Resource = fallbackResource, RootResource = fallbackResource };
                }

                return TranslatedContexts.GetValue(context, static firely => Build(firely));
            }

            private static IgnixaFhirEvaluationContext Build(FirelyEvaluationContext firely)
            {
                IElement resource = firely.Resource == null ? null : IgnixaElementAccessor.ToNative(firely.Resource);
                IElement rootResource = firely.RootResource == null ? resource : IgnixaElementAccessor.ToNative(firely.RootResource);

                var context = new IgnixaFhirEvaluationContext
                {
                    Resource = resource,
                    RootResource = rootResource,
                };

                // resolve() is used by reference search parameters; without the resolver those expressions
                // silently yield nothing rather than failing, so the wiring is deliberately explicit here.
                if (firely is FirelyFhirEvaluationContext fhirContext && fhirContext.ElementResolver != null)
                {
                    Func<string, ITypedElement> resolver = fhirContext.ElementResolver;

                    return context.WithElementResolver(reference =>
                    {
                        ITypedElement resolved = resolver(reference);
                        return resolved == null ? null : IgnixaElementAccessor.ToNative(resolved);
                    });
                }

                return context;
            }
        }
    }
}
