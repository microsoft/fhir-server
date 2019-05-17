// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Features.Search.Converters
{
    /// <summary>
    /// Provides mechanisms to access FHIR element type converter.
    /// </summary>
    public interface IFhirElementToSearchValueTypeConverterManager
    {
        /// <summary>
        /// Gets the converter associated with the <paramref name="fhirElementType"/>.
        /// </summary>
        /// <param name="fhirElementType">The FHIR element type whose associated converter to get.</param>
        /// <param name="searchValueType">The type of the search value that the converter creates</param>
        /// <param name="converter">When this method returns, contains the converter associated with the FHIR element type if the FHIR element type exists; otherwise, <c>null</c>.</param>
        /// <returns><c>true</c> if the converter exists; otherwise, <c>false</c>.</returns>
        bool TryGetConverter(Type fhirElementType, Type searchValueType, out IFhirElementToSearchValueTypeConverter converter);
    }
}
