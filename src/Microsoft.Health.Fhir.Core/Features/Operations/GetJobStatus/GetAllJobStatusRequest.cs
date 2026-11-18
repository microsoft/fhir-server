// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;

namespace Microsoft.Health.Fhir.Core.Features.Operations.GetJobStatus
{
    /// <summary>
    /// Request to get a page of async job statuses.
    /// </summary>
    public class GetAllJobStatusRequest : IRequest<GetAllJobStatusResponse>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GetAllJobStatusRequest"/> class.
        /// </summary>
        /// <param name="jobType">The operation name to filter by, or null for all supported job types.</param>
        /// <param name="continuationToken">The token returned by the previous page, or null for the first page.</param>
        public GetAllJobStatusRequest(string jobType = null, string continuationToken = null)
        {
            JobType = jobType;
            ContinuationToken = continuationToken;
        }

        /// <summary>
        /// Gets the operation name to filter by.
        /// </summary>
        public string JobType { get; }

        /// <summary>
        /// Gets the continuation token.
        /// </summary>
        public string ContinuationToken { get; }
    }
}
