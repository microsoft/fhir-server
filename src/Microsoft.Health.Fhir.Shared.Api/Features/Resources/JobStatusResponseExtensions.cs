// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Operations.GetJobStatus;

namespace Microsoft.Health.Fhir.Api.Features.Resources
{
    /// <summary>
    /// Extension methods for job status responses.
    /// </summary>
    public static class JobStatusResponseExtensions
    {
        /// <summary>
        /// Converts a list of JobStatusInfo to a result object for JSON serialization.
        /// </summary>
        /// <param name="jobs">The list of job status information.</param>
        /// <returns>An object suitable for JSON serialization.</returns>
        public static object ToJobStatusResult(this IReadOnlyList<JobStatusInfo> jobs)
        {
            return new
            {
                jobs = jobs.Select(j => new
                {
                    jobId = j.JobId,
                    groupId = j.GroupId,
                    jobType = j.JobType,
                    queueType = j.QueueType.ToString(),
                    status = j.Status.ToString(),
                    contentLocation = j.ContentLocation?.ToString(),
                    createDate = j.CreateDate,
                    startDate = j.StartDate,
                    endDate = j.EndDate,
                }).ToList(),
            };
        }
    }
}
