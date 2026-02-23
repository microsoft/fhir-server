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
        /// Gets the status information for all async jobs (Export, Import, Reindex, BulkDelete, BulkUpdate).
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>A list of job status information.</returns>
        Task<IReadOnlyList<JobStatusInfo>> GetAllJobStatusAsync(CancellationToken cancellationToken);
    }
}
