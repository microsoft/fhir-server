// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Expressions;
using Ignixa.FhirPath.Parser;
using Microsoft.Health.Fhir.Core.Features.Search.FhirPath;
using FirelyEvaluationContext = Hl7.FhirPath.EvaluationContext;

namespace Microsoft.Health.Fhir.Ignixa.FhirPath;

/// <summary>
/// Ignixa-based implementation of <see cref="IFhirPathProvider"/>.
/// </summary>
/// <remarks>
/// <para>
/// This provider uses Ignixa's FHIRPath engine which provides:
/// </para>
/// <list type="bullet">
/// <item><description>Full FHIRPath 2.0 specification support</description></item>
/// <item><description>Delegate compilation for ~80% of common search patterns</description></item>
/// <item><description>Native IElement evaluation without conversion overhead</description></item>
/// <item><description>Expression caching for performance</description></item>
/// </list>
/// </remarks>
public sealed class IgnixaFhirPathProvider : IFhirPathProvider
{
    private readonly ISchema _schema;
    private readonly FhirPathParser _parser;
    private readonly FhirPathEvaluator _evaluator;
    private readonly FhirPathDelegateCompiler _delegateCompiler;
    private readonly ConcurrentDictionary<string, ICompiledFhirPath> _cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="IgnixaFhirPathProvider"/> class.
    /// </summary>
    /// <param name="schema">The Ignixa schema for type metadata.</param>
    public IgnixaFhirPathProvider(ISchema schema)
    {
        EnsureArg.IsNotNull(schema, nameof(schema));

        _schema = schema;
        _parser = new FhirPathParser();
        _evaluator = new FhirPathEvaluator();
        _delegateCompiler = new FhirPathDelegateCompiler(_evaluator);
        _cache = new ConcurrentDictionary<string, ICompiledFhirPath>(StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public ICompiledFhirPath Compile(string expression)
    {
        EnsureArg.IsNotNullOrWhiteSpace(expression, nameof(expression));

        return _cache.GetOrAdd(expression, expr =>
        {
            var ast = _parser.Parse(expr);
            var compiledDelegate = _delegateCompiler.TryCompile(ast);
            return new IgnixaCompiledFhirPath(expr, ast, compiledDelegate, _evaluator, _schema);
        });
    }

    /// <inheritdoc />
    public IEnumerable<ITypedElement> Evaluate(ITypedElement element, string expression, FirelyEvaluationContext? context = null)
    {
        EnsureArg.IsNotNull(element, nameof(element));
        EnsureArg.IsNotNullOrWhiteSpace(expression, nameof(expression));

        var compiled = Compile(expression);
        return compiled.Evaluate(element, context);
    }

    /// <inheritdoc />
    public T? Scalar<T>(ITypedElement element, string expression, FirelyEvaluationContext? context = null)
    {
        EnsureArg.IsNotNull(element, nameof(element));
        EnsureArg.IsNotNullOrWhiteSpace(expression, nameof(expression));

        var compiled = Compile(expression);
        return compiled.Scalar<T>(element, context);
    }

    /// <inheritdoc />
    public bool Predicate(ITypedElement element, string expression, FirelyEvaluationContext? context = null)
    {
        EnsureArg.IsNotNull(element, nameof(element));
        EnsureArg.IsNotNullOrWhiteSpace(expression, nameof(expression));

        var compiled = Compile(expression);
        return compiled.Predicate(element, context);
    }
}
