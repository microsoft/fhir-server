// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.ElementModel;
using Hl7.FhirPath;

namespace Microsoft.Health.Fhir.Core.Features.Search.FhirPath
{
    /// <summary>
    /// Provides FHIRPath expression compilation and evaluation services.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This interface abstracts the FHIRPath engine implementation, allowing different
    /// providers (Firely SDK, Ignixa) to be used interchangeably. The provider handles:
    /// </para>
    /// <list type="bullet">
    /// <item><description>Expression parsing and compilation</description></item>
    /// <item><description>Expression caching for performance</description></item>
    /// <item><description>Expression evaluation against FHIR resources</description></item>
    /// </list>
    /// </remarks>
    public interface IFhirPathProvider
    {
        /// <summary>
        /// Compiles a FHIRPath expression string into a reusable compiled form.
        /// </summary>
        /// <param name="expression">The FHIRPath expression to compile.</param>
        /// <returns>A compiled FHIRPath expression that can be evaluated multiple times.</returns>
        /// <exception cref="System.FormatException">Thrown when the expression is invalid.</exception>
        /// <remarks>
        /// Implementations should cache compiled expressions to avoid repeated parsing.
        /// The same compiled expression can be evaluated against multiple resources.
        /// </remarks>
        ICompiledFhirPath Compile(string expression);

        /// <summary>
        /// Evaluates a FHIRPath expression against an element and returns matching elements.
        /// </summary>
        /// <param name="element">The element to evaluate against.</param>
        /// <param name="expression">The FHIRPath expression string.</param>
        /// <param name="context">Optional evaluation context with variables.</param>
        /// <returns>An enumerable of elements matching the expression.</returns>
        /// <remarks>
        /// This is a convenience method that compiles and evaluates in one call.
        /// For repeated evaluations of the same expression, use <see cref="Compile"/> instead.
        /// </remarks>
        IEnumerable<ITypedElement> Evaluate(ITypedElement element, string expression, EvaluationContext context = null);

        /// <summary>
        /// Evaluates a FHIRPath expression and returns a scalar value.
        /// </summary>
        /// <typeparam name="T">The expected return type.</typeparam>
        /// <param name="element">The element to evaluate against.</param>
        /// <param name="expression">The FHIRPath expression string.</param>
        /// <param name="context">Optional evaluation context with variables.</param>
        /// <returns>The scalar result of the evaluation, or default if not found.</returns>
        T Scalar<T>(ITypedElement element, string expression, EvaluationContext context = null);

        /// <summary>
        /// Evaluates a FHIRPath predicate expression and returns a boolean result.
        /// </summary>
        /// <param name="element">The element to evaluate against.</param>
        /// <param name="expression">The FHIRPath predicate expression.</param>
        /// <param name="context">Optional evaluation context with variables.</param>
        /// <returns>True if the predicate matches; otherwise false.</returns>
        bool Predicate(ITypedElement element, string expression, EvaluationContext context = null);
    }
}
