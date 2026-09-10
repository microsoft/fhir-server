// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Registration
{
    /// <summary>
    /// The effective runtime state of the FHIR service.
    /// </summary>
    public enum FhirRuntimeState
    {
        /// <summary>
        /// The FHIR service is active.
        /// </summary>
        Active,

        /// <summary>
        /// The FHIR service is deprecated.
        /// </summary>
        Deprecated,
    }
}
