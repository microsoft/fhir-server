// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Api.Configs
{
    /// <summary>
    /// UI related configuration.
    /// </summary>
    public class FeatureConfiguration
    {
        /// <summary>
        /// Gets or sets a value indicating whether the UI is supported or not.
        /// </summary>
        public bool SupportsUI { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether XML is supported or not.
        /// </summary>
        public bool SupportsXml { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether anonymized export is enabled or not.
        /// </summary>
        public bool SupportsAnonymizedExport { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to enable Asp.Net Core endpoint routing.
        /// </summary>
        public bool EnableEndpointRouting { get; }
    }
}
