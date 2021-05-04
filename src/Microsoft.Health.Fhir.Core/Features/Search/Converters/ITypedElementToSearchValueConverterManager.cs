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
    public interface ITypedElementToSearchValueConverterManager
    {
        /// <summary>
        /// Gets the converter associated with the <paramref name="fhirType"/>.
        /// </summary>
        /// <param name="fhirType">The FHIR type whose associated converter to get.</param>
        /// <param name="searchValueType">The type of the search value that the converter creates</param>
        /// <param name="converter">When this method returns, contains the converter associated with the FHIR type if the FHIR type exists; otherwise, <c>null</c>.</param>
        /// <returns><c>true</c> if the converter exists; otherwise, <c>false</c>.</returns>
        bool TryGetConverter(string fhirType, Type searchValueType, out ITypedElementToSearchValueConverter converter);
    }
}
