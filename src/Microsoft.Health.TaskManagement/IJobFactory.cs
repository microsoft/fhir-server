// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.JobManagement
{
    /// <summary>
    /// Interface for factory to create job from job information
    /// </summary>
    public interface IJobFactory
    {
        /// <summary>
        /// Create new job from JobInfo
        /// </summary>
        /// <param name="jobInfo">Job information payload.</param>
        /// <returns>Job for execution.</returns>
        IJob Create(JobInfo jobInfo);
    }
}
