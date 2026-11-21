// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Ignixa.Features.FhirPackages
{
    /// <summary>
    /// Trust and authentication settings for one npm source.
    /// </summary>
    public sealed class FhirPackageSourceOptions
    {
        /// <summary>
        /// Gets or sets the HTTPS registry directory URI.
        /// </summary>
        public string Registry { get; set; }

        /// <summary>
        /// Gets the HTTPS directory prefixes allowed for package artifacts.
        /// </summary>
        public ICollection<string> AllowedArtifactPrefixes { get; } = new List<string>();

        /// <summary>
        /// Gets or sets the Azure token scope, or <c>null</c> for an unauthenticated registry.
        /// Tokens are sent only to URLs under the registry directory.
        /// </summary>
        public string TokenScope { get; set; }

        /// <summary>
        /// Gets or sets the maximum number of redirects to approved artifact prefixes.
        /// </summary>
        public int MaxRedirects { get; set; }
    }
}
