// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;

namespace Microsoft.Health.Fhir.Core.Features.Operations.GetJobStatus
{
    /// <summary>
    /// Handler for getting all async job statuses.
    /// </summary>
    public class GetAllJobStatusHandler : IRequestHandler<GetAllJobStatusRequest, GetAllJobStatusResponse>
    {
        private readonly IJobStatusService _jobStatusService;

        /// <summary>
        /// Initializes a new instance of the <see cref="GetAllJobStatusHandler"/> class.
        /// </summary>
        /// <param name="jobStatusService">The job status service.</param>
        public GetAllJobStatusHandler(IJobStatusService jobStatusService)
        {
            _jobStatusService = EnsureArg.IsNotNull(jobStatusService, nameof(jobStatusService));
        }

        /// <inheritdoc />
        public async Task<GetAllJobStatusResponse> Handle(GetAllJobStatusRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            var jobs = await _jobStatusService.GetAllJobStatusAsync(cancellationToken);

            return new GetAllJobStatusResponse(jobs);
        }
    }
}
