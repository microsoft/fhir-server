// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Web.Configs
{
    /// <summary>
    /// Web project related configuration.
    /// </summary>
    public class WebFeatureConfiguration
    {
        /// <summary>
        /// Gets or sets a value indicating whether the security controllers should be enabled
        /// </summary>
        public bool SecurityControllersEnabled { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the use the app settings as the source for roles
        /// </summary>
        public bool UseAppSettingsRoleStore { get; set; }
    }
}
