// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Operations.Reindex.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Core.Features.Operations
{
    public interface IFhirOperationDataStore
    {
        /// <summary>
        /// Creates a new export job.
        /// </summary>
        /// <param name="jobRecord">The job record.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>An instance of newly created export job.</returns>
        Task<ExportJobOutcome> CreateExportJobAsync(ExportJobRecord jobRecord, CancellationToken cancellationToken);

        /// <summary>
        /// Gets an export job by id.
        /// </summary>
        /// <param name="id">The id of the job.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>An instance of the existing export job.</returns>
        /// <exception cref="JobNotFoundException"> thrown when the specific <paramref name="id"/> is not found. </exception>
        Task<ExportJobOutcome> GetExportJobByIdAsync(string id, CancellationToken cancellationToken);

        /// <summary>
        /// Updates an existing export job.
        /// </summary>
        /// <param name="jobRecord">The job record.</param>
        /// <param name="eTag">The eTag used for optimistic concurrency.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>An instance of the updated export job.</returns>
        Task<ExportJobOutcome> UpdateExportJobAsync(ExportJobRecord jobRecord, WeakETag eTag, CancellationToken cancellationToken);

        /// <summary>
        /// Commits a new reindex job record to the data store.
        /// </summary>
        /// <param name="jobRecord">The reindex job record</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>A newly created reindex job record</returns>
        Task<ReindexJobWrapper> CreateReindexJobAsync(ReindexJobRecord jobRecord, CancellationToken cancellationToken);

        /// <summary>
        /// Acquires export jobs.
        /// </summary>
        /// <param name="numberOfJobsToAcquire">The number of jobs to acquire.</param>
        /// <param name="jobHeartbeatTimeoutThreshold">The job heartbeat timeout threshold.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A list of acquired export job.</returns>
        Task<IReadOnlyCollection<ExportJobOutcome>> AcquireExportJobsAsync(ushort numberOfJobsToAcquire, TimeSpan jobHeartbeatTimeoutThreshold, CancellationToken cancellationToken);

        /// <summary>
        /// Updates an existing reindex job record in the data store.
        /// </summary>
        /// <param name="jobRecord">The updated job record</param>
        /// <param name="eTag">current eTag value</param>
        /// <param name="cancellationToken">the cancellation token</param>
        /// <returns>An instance of the updated job record</returns>
        Task<ReindexJobWrapper> UpdateReindexJobAsync(ReindexJobRecord jobRecord, WeakETag eTag, CancellationToken cancellationToken);

        /// <summary>
        /// Acquires reindex jobs.
        /// </summary>
        /// <param name="maximumNumberOfConcurrentJobsAllowed">The maximum number of concurrent reindex jobs allowed.</param>
        /// <param name="jobHeartbeatTimeoutThreshold">The job heartbeat timeout threshold.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A list of acquired reindex jobs.</returns>
        Task<IReadOnlyCollection<ReindexJobWrapper>> AcquireReindexJobsAsync(ushort maximumNumberOfConcurrentJobsAllowed, TimeSpan jobHeartbeatTimeoutThreshold, CancellationToken cancellationToken);

        /// <summary>
        /// Gets a reindex job by id.
        /// </summary>
        /// <param name="jobId">The id of the job.</param>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>The reindex job details.</returns>
        Task<ReindexJobWrapper> GetReindexJobByIdAsync(string jobId, CancellationToken cancellationToken);

        /// <summary>
        /// Queries the datastore for any reindex job documents with a status of running, queued or paused
        /// </summary>
        /// <param name="cancellationToken">The cancellation token</param>
        /// <returns>True if any found, along with id of the job</returns>
        Task<(bool found, string id)> CheckActiveReindexJobsAsync(CancellationToken cancellationToken);
    }
}
