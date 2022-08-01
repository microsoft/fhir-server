// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class TerminologyOperationConfiguration
    {
        /// <summary>
        /// Url indicating an external terminology service that can be used for terminology operations
        /// </summary>
        public string ExternalTerminologyServer { get; set; } = null;

        /// <summary>
        /// Contains validate operation configuration settings
        /// </summary>
        public ValidateOperationConfiguration Validate { get; set; } = new ValidateOperationConfiguration();

        /// <summary>
        /// True if validate-code is enabled, else false
        /// </summary>
        public bool ValidateCodeEnabled { get; set; } = false;

        /// <summary>
        /// True if lookup is enabled, else false
        /// </summary>
        public bool LookupEnabled { get; set; } = false;

        /// <summary>
        /// True if expand is enabled, else false
        /// </summary>
        public bool ExpandEnabled { get; set; } = false;
    }
}
