// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Models
{
    /// <summary>
    /// Defines the possible specifications of Fhir.
    /// </summary>
    public enum FhirSpecification
    {
        /// <summary>
        /// The Stu3 Fhir specification.
        /// </summary>
        Stu3,

        /// <summary>
        /// The R4 Fhir specification.
        /// </summary>
        R4,

        /// <summary>
        /// The R5 Fhir specification.
        /// </summary>
        R5,
    }
}
