// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.FhirPath;
using Ignixa.Abstractions;
using Ignixa.Extensions.FirelySdk;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.FhirPath;
using FirelyEvaluationContext = Hl7.FhirPath.EvaluationContext;
using FirelyFhirEvaluationContext = Hl7.Fhir.FhirPath.FhirEvaluationContext;
using IgnixaEvaluationContext = Ignixa.FhirPath.Evaluation.EvaluationContext;
using IgnixaFhirEvaluationContext = Ignixa.FhirPath.Evaluation.FhirEvaluationContext;

namespace Microsoft.Health.Fhir.Ignixa.FhirPath;

/// <summary>
/// Ignixa-based compiled FHIRPath expression.
/// </summary>
internal sealed class IgnixaCompiledFhirPath : ICompiledFhirPath
{
    private readonly Expression _ast;
    private readonly Func<IElement, IgnixaEvaluationContext, IEnumerable<IElement>>? _compiledDelegate;
    private readonly FhirPathEvaluator _evaluator;
    private readonly ISchema _schema;

    public IgnixaCompiledFhirPath(
        string expression,
        Expression ast,
        Func<IElement, IgnixaEvaluationContext, IEnumerable<IElement>>? compiledDelegate,
        FhirPathEvaluator evaluator,
        ISchema schema)
    {
        Expression = expression;
        _ast = ast;
        _compiledDelegate = compiledDelegate;
        _evaluator = evaluator;
        _schema = schema;
    }

    /// <inheritdoc />
    public string Expression { get; }

    /// <inheritdoc />
    public IEnumerable<ITypedElement> Evaluate(ITypedElement element, FirelyEvaluationContext? context = null)
    {
        EnsureArg.IsNotNull(element, nameof(element));

        // Convert ITypedElement to IElement using schema
        var ignixaElement = ConvertToElement(element);

        // Create Ignixa evaluation context
        var ignixaContext = CreateIgnixaContext(ignixaElement, context);

        // Evaluate using compiled delegate (fast path) or interpreter (fallback)
        var results = _compiledDelegate != null
            ? _compiledDelegate(ignixaElement, ignixaContext)
            : _evaluator.Evaluate(ignixaElement, _ast, ignixaContext);

        // Convert results back to ITypedElement
        foreach (var result in results)
        {
            yield return result.ToTypedElement();
        }
    }

    /// <inheritdoc />
    public T? Scalar<T>(ITypedElement element, FirelyEvaluationContext? context = null)
    {
        var results = Evaluate(element, context);
        var first = results.FirstOrDefault();

        if (first == null)
        {
            return default;
        }

        // Get the scalar value
        var value = first.Value;
        if (value is T typedValue)
        {
            return typedValue;
        }

        // Try conversion for common types
        if (value != null && typeof(T) == typeof(string))
        {
            return (T)(object)value.ToString()!;
        }

        return default;
    }

    /// <inheritdoc />
    public bool Predicate(ITypedElement element, FirelyEvaluationContext? context = null)
    {
        var results = Evaluate(element, context).ToList();

        // FHIRPath predicate semantics:
        // - Empty collection = false
        // - Single boolean = that boolean value
        // - Non-empty collection of non-booleans = true
        if (results.Count == 0)
        {
            return false;
        }

        if (results.Count == 1 && results[0].Value is bool boolValue)
        {
            return boolValue;
        }

        // Non-empty collection = true (exists semantics)
        return true;
    }

    /// <summary>
    /// Converts an ITypedElement to an IElement using the schema.
    /// </summary>
    private static IElement ConvertToElement(ITypedElement typedElement)
    {
        return typedElement.ToIgnixaElement();
    }

    /// <summary>
    /// Creates an Ignixa evaluation context from a Firely context.
    /// </summary>
    private static IgnixaFhirEvaluationContext CreateIgnixaContext(
        IElement element,
        FirelyEvaluationContext? firelyContext)
    {
        var ignixaContext = new IgnixaFhirEvaluationContext
        {
            Resource = element,
            RootResource = element,
        };

        // Transfer variables and resolver from Firely context if present
        if (firelyContext != null)
        {
            // Set %resource variable to the context resource
            if (firelyContext.Resource != null)
            {
                var resourceElement = ConvertToElement(firelyContext.Resource);
                ignixaContext.Resource = resourceElement;
                ignixaContext.SetEnvironmentVariable("resource", resourceElement);
            }

            // Transfer the element resolver for resolve() function support
            // The context may be FhirEvaluationContext (which has ElementResolver) or base EvaluationContext
            // Firely's resolver: Func<string, ITypedElement> (from LightweightReferenceToElementResolver.Resolve)
            // Ignixa's resolver: Func<string, IElement?>
            // Wrap to convert between the two
            if (firelyContext is FirelyFhirEvaluationContext fhirContext && fhirContext.ElementResolver != null)
            {
                ignixaContext.ElementResolver = referenceString =>
                {
                    // Call Firely's resolver which returns ITypedElement
                    var resolvedTypedElement = fhirContext.ElementResolver(referenceString);

                    // Convert to IElement if not null
                    if (resolvedTypedElement != null)
                    {
                        return ConvertToElement(resolvedTypedElement);
                    }

                    return null;
                };
            }
        }

        return ignixaContext;
    }
}
