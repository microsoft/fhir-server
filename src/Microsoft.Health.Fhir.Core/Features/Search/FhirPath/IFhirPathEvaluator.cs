// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Search.FhirPath
{
    /// <summary>
    /// Compiles FHIRPath expressions for evaluation during search indexing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the seam that lets search indexing run on either the Firely or the Ignixa FHIRPath engine,
    /// selected once at startup from the configured FHIR SDK provider. It is deliberately narrow: it
    /// exposes only compilation, because that is all <see cref="TypedElementSearchIndexer"/> requires. It
    /// is not a general FHIR SDK facade, and ad-hoc FHIRPath elsewhere in the server
    /// (<c>ResourceElement.Scalar</c>/<c>Predicate</c>, conformance, the search value converters)
    /// intentionally remains on Firely.
    /// </para>
    /// <para>
    /// Implementations must be thread-safe and are expected to cache compiled expressions, since the same
    /// set of search parameter expressions is evaluated for every indexed resource.
    /// </para>
    /// </remarks>
    public interface IFhirPathEvaluator
    {
        /// <summary>
        /// Compiles a FHIRPath expression, returning a cached instance when the expression has been seen before.
        /// </summary>
        /// <param name="expression">The FHIRPath expression to compile.</param>
        /// <returns>The compiled expression.</returns>
        ICompiledFhirPath Compile(string expression);
    }
}
