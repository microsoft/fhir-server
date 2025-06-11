// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using MediatR;
using Microsoft.Health.JobManagement;

namespace Microsoft.Health.TaskManagement
{
    /// <summary>
    /// A notification that is raised when a job is completed.
    /// </summary>
    public class JobCompletedNotification : INotification
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="JobCompletedNotification"/> class.
        /// </summary>
        /// <param name="jobInfo">The completed job info.</param>
        public JobCompletedNotification(JobInfo jobInfo)
        {
            JobInfo = jobInfo;
        }

        /// <summary>
        /// Gets the completed job information.
        /// </summary>
        public JobInfo JobInfo { get; }
    }
}
