// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Configs
{
    /// <summary>
    /// core feature configurations
    /// </summary>
    public class CoreFeatureConfiguration
    {
        /// <summary>
        /// Gets or sets a value indicating whether Batch is enabled or not.
        /// </summary>
        public bool SupportsBatch { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether Transaction is enabled or not.
        /// </summary>
        public bool SupportsTransaction { get; set; }
    }
}
