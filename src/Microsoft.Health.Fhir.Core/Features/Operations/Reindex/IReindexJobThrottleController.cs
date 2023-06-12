// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reindex
{
    public interface IReindexJobThrottleController
    {
        void Initialize(ReindexProcessingJobDefinition reindexJobRecord, int? provisionedDatastoreCapacity);

        /// <summary>
        /// Gets the current delay to achieve a target resource utilization
        /// </summary>
        /// <returns>delay in milliseconds</returns>
        int GetThrottleBasedDelay();

        /// <summary>
        /// Captures the currently recorded database consumption
        /// </summary>
        /// <returns>Returns an average database resource consumtion per second</returns>
        double UpdateDatastoreUsage();

        /// <summary>
        /// If one single query consumes more than the target datastore resources
        /// reduce the batch size to help acheive the desired level of usage
        /// If one query is not too expensive, this will return the same number as is configured
        /// for the Reindex job.
        /// </summary>
        /// <returns>The query batch size</returns>
        public uint GetThrottleBatchSize();
    }
}
