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
    /// Represents a compiled FHIRPath expression that can be evaluated multiple times.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Compiled expressions are cached and reused for performance. The same compiled
    /// expression can be evaluated against different resources and contexts.
    /// </para>
    /// <para>
    /// Implementations may use different optimization strategies (delegate compilation,
    /// interpreted evaluation) depending on the expression complexity.
    /// </para>
    /// </remarks>
    public interface ICompiledFhirPath
    {
        /// <summary>
        /// Gets the original FHIRPath expression string.
        /// </summary>
        string Expression { get; }

        /// <summary>
        /// Evaluates the compiled expression against an element.
        /// </summary>
        /// <param name="element">The element to evaluate against.</param>
        /// <param name="context">Optional evaluation context with variables like %resource.</param>
        /// <returns>An enumerable of elements matching the expression.</returns>
        IEnumerable<ITypedElement> Evaluate(ITypedElement element, EvaluationContext context = null);

        /// <summary>
        /// Evaluates the compiled expression and returns a scalar value.
        /// </summary>
        /// <typeparam name="T">The expected return type.</typeparam>
        /// <param name="element">The element to evaluate against.</param>
        /// <param name="context">Optional evaluation context with variables.</param>
        /// <returns>The scalar result of the evaluation, or default if not found.</returns>
        T Scalar<T>(ITypedElement element, EvaluationContext context = null);

        /// <summary>
        /// Evaluates the compiled expression as a predicate.
        /// </summary>
        /// <param name="element">The element to evaluate against.</param>
        /// <param name="context">Optional evaluation context with variables.</param>
        /// <returns>True if the predicate matches; otherwise false.</returns>
        bool Predicate(ITypedElement element, EvaluationContext context = null);
    }
}
