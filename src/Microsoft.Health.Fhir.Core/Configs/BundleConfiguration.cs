// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class BundleConfiguration
    {
        public int EntryLimit { get; set; } = 500;

        public int MaxExecutionTimeInSeconds { get; set; } = 100;

        /// <summary>
        /// Gets or sets a value indicating whether bundle orchestrator is enabled or not.
        /// </summary>
        public bool SupportsBundleOrchestrator { get; set; }

        // The default bundle processing logic for Transactions is set to Parallel, as by the FHIR specification, a transactional bundle
        // cannot contain duplicated operations under the same bundle.
        public BundleProcessingLogic TransactionDefaultProcessingLogic { get; set; } = BundleProcessingLogic.Parallel;

        // The default bundle processing logic for Batches is set to Sequential, as current customer can have resource identities overlaps
        // (including resolved identities from conditional update/delete, which are not allowed in a transaction bundle).
        public BundleProcessingLogic BatchDefaultProcessingLogic { get; set; } = BundleProcessingLogic.Sequential;
    }
}
