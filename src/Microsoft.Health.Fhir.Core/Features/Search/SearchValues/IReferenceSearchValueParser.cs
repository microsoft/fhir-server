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
        /// <param name="value">The string to be parsed.</param>
        /// <param name="context">The semantic origin of the value, which determines how ids and literals are classified.</param>
        /// <returns>An instance of <see cref="ReferenceSearchValue"/>.</returns>
        /// <exception cref="Validation.FhirPrimitiveTypes.ResourceIdentityFormatException">A recognized resource id is not valid under the resource id policy.</exception>
        ReferenceSearchValue Parse(string value, ReferenceParseContext context);
    }
}
