// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class BundleConfiguration
    {
        public const int DefaultConditionalOperationGroupSize = 50;

        public int EntryLimit { get; set; } = 500;

        /// <summary>
        /// Gets or sets the maximum number of entries allowed when high-latency bundle processing is enabled.
        /// </summary>
        public int EntryLimitExpanded { get; set; } = 500;

        public int MaxExecutionTimeInSeconds { get; set; } = 100;

        // The default bundle processing logic for Transactions is set to Parallel, as by the FHIR specification, a transactional bundle
        // cannot contain duplicated operations under the same bundle.
        public BundleProcessingLogic TransactionDefaultProcessingLogic { get; set; } = BundleProcessingLogic.Parallel;

        // The default bundle processing logic for Batches is set to Sequential, as current customer can have resource identities overlaps
        // (including resolved identities from conditional update/delete, which are not allowed in a transaction bundle).
        public BundleProcessingLogic BatchDefaultProcessingLogic { get; set; } = BundleProcessingLogic.Sequential;

        public int BundleExpandedGroupSize { get; set; } = 50;

        public int BundleExpandedGroupDelayInMilliseconds { get; set; } = 07;

        /// <summary>
        /// Gets or sets a value indicating whether conditional-operation throttling is enabled.
        /// </summary>
        public bool IsConditionalOperationThrottlingEnabled { get; set; } = true;

        /// <summary>
        /// Gets or sets the number of conditional operations dispatched between pacing delays in a parallel bundle.
        /// </summary>
        public int ConditionalOperationGroupSize { get; set; } = DefaultConditionalOperationGroupSize;

        /// <summary>
        /// Gets or sets the pacing delay, in milliseconds, between groups of conditional operations.
        /// </summary>
        public int ConditionalOperationGroupDelayInMilliseconds { get; set; } = 15;
    }
}
