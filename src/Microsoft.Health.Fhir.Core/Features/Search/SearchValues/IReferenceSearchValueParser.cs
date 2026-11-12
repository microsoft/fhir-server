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
        /// Parses the string value to an instance of <see cref="ReferenceSearchValue"/>.
        /// </summary>
        /// <param name="s">The string to be parsed.</param>
        /// <returns>An instance of <see cref="ReferenceSearchValue"/>.</returns>
        ReferenceSearchValue Parse(string s);

        /// <summary>
        /// Parses the value of a Reference element to an instance of <see cref="ReferenceSearchValue"/>.
        /// A Reference element must be indexed whole, so a value that is too long to index is rejected.
        /// </summary>
        /// <param name="s">The string to be parsed.</param>
        /// <returns>An instance of <see cref="ReferenceSearchValue"/>.</returns>
        ReferenceSearchValue ParseResourceReference(string s);
    }
}
