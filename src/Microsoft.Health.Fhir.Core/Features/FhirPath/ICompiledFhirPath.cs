// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.ElementModel;
using Hl7.FhirPath;

namespace Microsoft.Health.Fhir.Core.Features.FhirPath
{
    /// <summary>
    /// Represents a compiled FHIRPath expression.
    /// </summary>
    public interface ICompiledFhirPath
    {
        /// <summary>
        /// Gets the source expression.
        /// </summary>
        string Expression { get; }

        /// <summary>
        /// Evaluates the expression against an input element.
        /// </summary>
        /// <param name="input">The input element.</param>
        /// <param name="context">The evaluation context.</param>
        /// <returns>The selected elements.</returns>
        IEnumerable<ITypedElement> Select(ITypedElement input, EvaluationContext context = null);
    }
}
