// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Configs
{
    /// <summary>
    /// Selects the SDK provider independently for each migrated seam.
    /// </summary>
    public sealed class FhirSdkProviderConfiguration
    {
        /// <summary>
        /// Gets or sets the default provider.
        /// </summary>
        public FhirSdkProvider Default { get; set; } = FhirSdkProvider.Firely;

        /// <summary>
        /// Gets or sets the import provider override.
        /// </summary>
        public FhirSdkProvider? Import { get; set; }

        /// <summary>
        /// Gets or sets the FHIRPath provider override.
        /// </summary>
        public FhirSdkProvider? FhirPath { get; set; }

        /// <summary>
        /// Gets the effective import provider.
        /// </summary>
        public FhirSdkProvider EffectiveImport => Import ?? Default;

        /// <summary>
        /// Gets the effective FHIRPath provider.
        /// </summary>
        public FhirSdkProvider EffectiveFhirPath => FhirPath ?? Default;
    }
}
