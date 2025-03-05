// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Api.Configs
{
    /// <summary>
    /// Configuration values for optional network settings.
    /// The values are updated in workspace-platform(managed solution)
    /// </summary>
    public class NetworkConfiguration
    {
        /// <summary>
        /// Sets the service endpoint
        /// </summary>
        public Uri ServiceUrl { get; set; }

        /// <summary>
        /// Whether Private link is enabled.
        /// </summary>
        public bool IsPrivate { get; set; }
    }
}
