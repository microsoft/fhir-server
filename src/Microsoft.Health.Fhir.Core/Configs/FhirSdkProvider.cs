// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Configs
{
    /// <summary>
    /// Identifies the preferred FHIR SDK at feature seams that support provider selection.
    /// </summary>
    public enum FhirSdkProvider
    {
        /// <summary>
        /// Use the Firely SDK implementation.
        /// </summary>
        Firely = 0,

        /// <summary>
        /// Use the Ignixa SDK implementation.
        /// </summary>
        Ignixa = 1,
    }
}
