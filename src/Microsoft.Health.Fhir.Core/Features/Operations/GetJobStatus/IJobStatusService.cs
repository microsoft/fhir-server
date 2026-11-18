// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Operations.GetJobStatus
{
    /// <summary>
    /// Service interface for retrieving job status information.
    /// </summary>
    public interface IJobStatusService
    {
        /// <summary>
        /// Gets a bounded page of async job groups, ordered by group ID and queue type descending.
        /// </summary>
        /// <param name="queueType">The job queue to filter by, or null for all supported queues.</param>
        /// <param name="count">The maximum number of groups to retrieve, including a possible lookahead group.</param>
        /// <param name="afterGroupId">The last group ID of the previous page, or null for the first page.</param>
        /// <param name="afterQueueType">The last queue type of the previous page, or null for the first page.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A list of job status information.</returns>
        Task<IReadOnlyList<JobStatusInfo>> GetAllJobStatusAsync(QueueType? queueType, int count, long? afterGroupId, QueueType? afterQueueType, CancellationToken cancellationToken);
    }
}
