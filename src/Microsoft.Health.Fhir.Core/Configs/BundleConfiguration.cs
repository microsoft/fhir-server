// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class BundleConfiguration
    {
        public const int DefaultConditionalOperationGroupSize = 50;

        public const long DefaultMaxBundleSizeBytes = 50 * 1024 * 1024;

        public const long DefaultMaxResourceSizeBytes = 10 * 1024 * 1024;

        public const int DefaultMaxSearchParametersPerResource = 1000;

        public int EntryLimit { get; set; } = 500;

        /// <summary>
        /// Gets or sets the maximum number of entries allowed when high-latency bundle processing is enabled.
        /// </summary>
        public int EntryLimitExpanded { get; set; } = 500;

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

        /// <summary>
        /// Gets or sets a value indicating whether bundles exceeding the size and search parameter limits are rejected.
        /// Bundle statistics are logged regardless of this value.
        /// </summary>
        public bool EnableSizeRestrictions { get; set; }

        /// <summary>
        /// Gets or sets the maximum bundle request body size, in bytes. Zero disables the limit.
        /// </summary>
        public long MaxBundleSizeBytes { get; set; } = DefaultMaxBundleSizeBytes;

        /// <summary>
        /// Gets or sets the maximum serialized size of a single bundle entry resource, in bytes. Zero disables the limit.
        /// </summary>
        public long MaxResourceSizeBytes { get; set; } = DefaultMaxResourceSizeBytes;

        /// <summary>
        /// Gets or sets the maximum number of search index values per bundle entry resource. Zero disables the limit.
        /// </summary>
        public int MaxSearchParametersPerResource { get; set; } = DefaultMaxSearchParametersPerResource;

        /// <summary>
        /// Throws when a bundle limit is configured with an invalid value.
        /// </summary>
        public void Validate()
        {
            if (MaxBundleSizeBytes < 0)
            {
                throw new InvalidOperationException($"FhirServer:Bundle:{nameof(MaxBundleSizeBytes)} must be zero or greater. Value: {MaxBundleSizeBytes}.");
            }

            if (MaxResourceSizeBytes < 0)
            {
                throw new InvalidOperationException($"FhirServer:Bundle:{nameof(MaxResourceSizeBytes)} must be zero or greater. Value: {MaxResourceSizeBytes}.");
            }

            if (MaxSearchParametersPerResource < 0)
            {
                throw new InvalidOperationException($"FhirServer:Bundle:{nameof(MaxSearchParametersPerResource)} must be zero or greater. Value: {MaxSearchParametersPerResource}.");
            }
        }
    }
}
