// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Operations.GetJobStatus
{
    /// <summary>
    /// Represents the response containing a list of job status information.
    /// </summary>
    public class GetAllJobStatusResponse
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GetAllJobStatusResponse"/> class.
        /// </summary>
        /// <param name="jobs">The list of job status information.</param>
        public GetAllJobStatusResponse(IReadOnlyList<JobStatusInfo> jobs)
        {
            Jobs = jobs;
        }

        /// <summary>
        /// Gets the list of job status information.
        /// </summary>
        public IReadOnlyList<JobStatusInfo> Jobs { get; }
    }
}
