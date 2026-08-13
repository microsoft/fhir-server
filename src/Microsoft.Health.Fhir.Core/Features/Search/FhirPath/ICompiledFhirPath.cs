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
    /// A FHIRPath expression that has been compiled by an <see cref="IFhirPathEvaluator"/> and can be
    /// evaluated repeatedly against different resources.
    /// </summary>
    /// <remarks>
    /// Implementations are expected to be thread-safe, since a single compiled instance is cached by the
    /// owning evaluator and reused across concurrent indexing operations.
    /// </remarks>
    public interface ICompiledFhirPath
    {
        /// <summary>
        /// Gets the original FHIRPath expression text this instance was compiled from.
        /// </summary>
        string Expression { get; }

        /// <summary>
        /// Evaluates the expression against <paramref name="input"/>.
        /// </summary>
        /// <param name="input">The element to evaluate against.</param>
        /// <param name="context">
        /// The evaluation context, supplying <c>%resource</c> and the <c>resolve()</c> element resolver.
        /// </param>
        /// <returns>The elements selected by the expression.</returns>
        IEnumerable<ITypedElement> Evaluate(ITypedElement input, EvaluationContext context);
    }
}
