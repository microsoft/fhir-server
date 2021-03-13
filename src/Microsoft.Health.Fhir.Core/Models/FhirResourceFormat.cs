// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Models
{
    /// <summary>
    /// Defines the possible formats of Fhir Resource.
    /// </summary>
    public enum FhirResourceFormat
    {
        /// <summary>
        /// The resource format is unknown.
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// The resource is in Xml format.
        /// </summary>
        Xml = 1,

        /// <summary>
        /// The resource is in Json format.
        /// </summary>
        Json = 2,
    }
}
