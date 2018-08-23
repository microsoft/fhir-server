// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Configs
{
    /// <summary>
    /// Conformance related configuration.
    /// </summary>
    public class ConformanceConfiguration
    {
        /// <summary>
        /// Gets or sets a value indicating whether to use strict conformance mode.
        /// </summary>
        public bool UseStrictConformance { get; set; } = true;
    }
}
