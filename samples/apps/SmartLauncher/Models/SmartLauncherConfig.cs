// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Internal.SmartLauncher.Models
{
    /// <summary>
    /// Configuration for the SMART on FHIR sample launcher.
    /// </summary>
    internal class SmartLauncherConfig
    {
#pragma warning disable CA1056 // URI-like properties should not be strings

        /// <summary>
        /// Gets or sets the FHIR server base URL.
        /// </summary>
        public string FhirServerUrl { get; set; }

        /// <summary>
        /// Gets or sets the default SMART app launch URL.
        /// </summary>
        public string DefaultSmartAppUrl { get; set; }

#pragma warning restore CA1056 // URI-like properties should not be strings

        /// <summary>
        /// Gets or sets the OAuth2 client ID registered with the identity provider.
        /// </summary>
        public string ClientId { get; set; }

        /// <summary>
        /// Gets or sets the client type: "public", "confidential-symmetric", or "confidential-asymmetric".
        /// </summary>
        public string ClientType { get; set; } = "public";

        /// <summary>
        /// Gets or sets the OAuth2 scopes to request during authorization.
        /// </summary>
        public string Scopes { get; set; } = "openid fhirUser launch/patient patient/*.rs offline_access";
    }
}
