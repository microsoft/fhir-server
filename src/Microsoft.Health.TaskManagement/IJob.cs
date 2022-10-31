// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.JobManagement
{
    /// <summary>
    /// Interface for execution job.
    /// </summary>
    public interface IJob
    {
        /// <summary>
        /// Execute the job.
        /// </summary>
        /// <param name="jobInfo">The Job Info.</param>
        /// <param name="progress">Report job progress.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Job result in string. </returns>
        public Task<string> ExecuteAsync(JobInfo jobInfo, IProgress<string> progress, CancellationToken cancellationToken);
    }
}
