// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Configs
{
    /// <summary>
    /// Selects the FHIR SDK used at each migrated feature seam, with a single global default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The migration described in ADR 2607 moves one seam at a time, and the seams do not have to move together.
    /// A single global switch forced an operator to adopt every migrated seam at once, which makes it impossible
    /// to enable the fast $import path while leaving search indexing on Firely, and impossible to roll a single
    /// seam back after a problem without giving up the others.
    /// </para>
    /// <para>
    /// <see cref="Default"/> applies to every seam that has no explicit override, so the common cases stay a
    /// one-line change: leave everything alone for Firely, or set <see cref="Default"/> to
    /// <see cref="FhirSdkProvider.Ignixa"/> to adopt every migrated seam. Overrides are for narrowing that
    /// choice per seam.
    /// </para>
    /// <para>
    /// Seams that have not been migrated yet are unaffected by any of these settings; they remain Firely-backed
    /// until they are migrated in their own change.
    /// </para>
    /// </remarks>
    public class FhirSdkProviderConfiguration
    {
        /// <summary>
        /// Gets or sets the provider used by any seam without an explicit override.
        /// Firely remains the default until the final migration cutover.
        /// </summary>
        public FhirSdkProvider Default { get; set; } = FhirSdkProvider.Firely;

        /// <summary>
        /// Gets or sets the provider used to parse resources for the <c>$import</c> operation, overriding
        /// <see cref="Default"/> when set.
        /// </summary>
        public FhirSdkProvider? Import { get; set; }

        /// <summary>
        /// Gets or sets the provider whose FHIRPath engine evaluates search parameter expressions during search
        /// indexing and reindex, overriding <see cref="Default"/> when set.
        /// </summary>
        public FhirSdkProvider? FhirPath { get; set; }

        /// <summary>
        /// Gets or sets the provider used to serialize the stored raw resource on write, overriding
        /// <see cref="Default"/> when set.
        /// </summary>
        public FhirSdkProvider? Serialization { get; set; }

        /// <summary>
        /// Gets the provider actually used for <c>$import</c> parsing.
        /// </summary>
        public FhirSdkProvider EffectiveImport => Import ?? Default;

        /// <summary>
        /// Gets the provider actually used for FHIRPath evaluation during search indexing.
        /// </summary>
        public FhirSdkProvider EffectiveFhirPath => FhirPath ?? Default;

        /// <summary>
        /// Gets the provider actually used to serialize the stored raw resource.
        /// </summary>
        public FhirSdkProvider EffectiveSerialization => Serialization ?? Default;
    }
}
