// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Health.Fhir.Core.Configs
{
    /// <summary>
    /// Selects the SDK provider for each migrated seam. Every seam uses <see cref="SystemDefault"/>
    /// unless its own override is set.
    /// </summary>
    public sealed class FhirSdkProviderConfiguration
    {
        private static readonly string[] KnownKeys = [nameof(SystemDefault), nameof(Import), nameof(FhirPath)];

        /// <summary>
        /// Gets or sets the provider used by every seam that has no override.
        /// </summary>
        public FhirSdkProvider SystemDefault { get; set; } = FhirSdkProvider.Firely;

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
        public FhirSdkProvider EffectiveImport => Import ?? SystemDefault;

        /// <summary>
        /// Gets the effective FHIRPath provider.
        /// </summary>
        public FhirSdkProvider EffectiveFhirPath => FhirPath ?? SystemDefault;

        /// <summary>
        /// Rejects provider settings that the configuration binder would otherwise ignore or misread:
        /// a single value instead of an object, unknown keys, and values that are not provider names.
        /// A rollback setting that is silently ignored would leave the server on the wrong provider.
        /// </summary>
        /// <param name="section">The <c>FhirServer:CoreFeatures:FhirSdkProvider</c> configuration section.</param>
        /// <exception cref="InvalidOperationException">The section contains an unsupported setting.</exception>
        public static void Validate(IConfigurationSection section)
        {
            ArgumentNullException.ThrowIfNull(section);

            if (section.Value is not null)
            {
                throw new InvalidOperationException(
                    $"'{section.Path}' must be an object, not a single value. Set '{section.Path}:{nameof(SystemDefault)}' instead.");
            }

            foreach (IConfigurationSection setting in section.GetChildren())
            {
                if (!KnownKeys.Contains(setting.Key, StringComparer.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"'{setting.Path}' is not a supported setting. Supported settings: {string.Join(", ", KnownKeys)}.");
                }

                // Match a single provider name exactly: Enum.TryParse would also accept numbers and
                // comma-separated names ("Firely, Ignixa" parses as Ignixa).
                if (!Enum.GetNames<FhirSdkProvider>().Contains(setting.Value?.Trim(), StringComparer.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"'{setting.Path}' has unsupported value '{setting.Value}'. Expected one of: {string.Join(", ", Enum.GetNames<FhirSdkProvider>())}.");
                }
            }
        }
    }
}
