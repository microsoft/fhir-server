// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Configs
{
    /// <summary>
    /// Configuration for a third-party SMART identity provider.
    /// </summary>
    public class SmartIdentityProviderConfiguration
    {
        /// <summary>
        /// Gets or sets the authority URL for the third-party identity provider.
        /// This overrides the default authority from SecurityConfiguration when specified.
        /// </summary>
        public string Authority { get; set; }

        /// <summary>
        /// Gets or sets the introspection endpoint URL for token introspection.
        /// </summary>
        public string Introspection { get; set; }

        /// <summary>
        /// Gets or sets the management endpoint URL for application management.
        /// </summary>
        public string Management { get; set; }

        /// <summary>
        /// Gets or sets the revocation endpoint URL for token revocation.
        /// </summary>
        public string Revocation { get; set; }
    }
}
