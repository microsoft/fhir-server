// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.FhirPath
{
    /// <summary>
    /// Compiles FHIRPath expressions for evaluation.
    /// </summary>
    public interface IFhirPathProvider
    {
        /// <summary>
        /// Compiles an expression into an executable handle.
        /// </summary>
        /// <param name="expression">The FHIRPath expression.</param>
        /// <returns>The compiled expression.</returns>
        ICompiledFhirPath Compile(string expression);
    }
}
