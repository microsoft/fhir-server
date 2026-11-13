// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Search.SearchValues
{
    /// <summary>
    /// Provides mechanism to parse a string to an instance of <see cref="ReferenceSearchValue"/>.
    /// </summary>
    public interface IReferenceSearchValueParser
    {
        /// <summary>
        /// Parses a reference search value and applies the resource-ID length policy to typed results.
        /// </summary>
        /// <param name="s">The string to be parsed.</param>
        /// <returns>An instance of <see cref="ReferenceSearchValue"/>.</returns>
        ReferenceSearchValue Parse(string s);

        /// <summary>
        /// Classifies a canonical, uri or url value without enforcing resource-ID length limits.
        /// This does not validate URI syntax and must not be used for Reference elements or reference queries.
        /// </summary>
        /// <param name="s">The string to be classified.</param>
        /// <returns>An instance of <see cref="ReferenceSearchValue"/> with the full captured component.</returns>
        ReferenceSearchValue ParseUri(string s);

        /// <summary>
        /// Parses a Reference element with the resource-ID length policy.
        /// Also rejects oversized untyped values and base URIs when rejecting long references is enabled.
        /// </summary>
        /// <param name="s">The string to be parsed.</param>
        /// <returns>An instance of <see cref="ReferenceSearchValue"/>.</returns>
        ReferenceSearchValue ParseResourceReference(string s);
    }
}
