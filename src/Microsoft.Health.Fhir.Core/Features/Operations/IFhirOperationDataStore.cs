// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
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
        /// Gets an export job by hash.
        /// </summary>
        /// <param name="hash">The hash of the job.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>An instance of the matching export job; otherwise, <c>null</c>.</returns>
        Task<ExportJobOutcome> GetExportJobByHashAsync(string hash, CancellationToken cancellationToken);

        /// <summary>
        /// Updates an existing export job.
        /// </summary>
        /// <param name="jobRecord">The job record.</param>
        /// <param name="eTag">The eTag used for optimistic concurrency.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>An instance of the updated export job.</returns>
        Task<ExportJobOutcome> UpdateExportJobAsync(ExportJobRecord jobRecord, WeakETag eTag, CancellationToken cancellationToken);

        /// <summary>
        /// Acquires export jobs.
        /// </summary>
        /// <param name="maximumNumberOfConcurrentJobsAllowed">The maximum number of concurrent jobs allowed.</param>
        /// <param name="jobHeartbeatTimeoutThreshold">The job heartbeat timeout threshold.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A list of acquired export job.</returns>
        Task<IReadOnlyCollection<ExportJobOutcome>> AcquireExportJobsAsync(ushort maximumNumberOfConcurrentJobsAllowed, TimeSpan jobHeartbeatTimeoutThreshold, CancellationToken cancellationToken);
    }
}
