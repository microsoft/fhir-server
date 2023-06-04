// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class BundleConfiguration
    {
        public int EntryLimit { get; set; } = 500;

        /// <summary>
        /// Gets or sets a value indicating whether bundle orchestrator is enabled or not.
        /// </summary>
        public bool SupportsBundleOrchestrator { get; set; }
    }
}
