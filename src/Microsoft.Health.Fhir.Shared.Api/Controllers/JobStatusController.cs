// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using EnsureThat;
using Medino;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Resources;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Operations.GetJobStatus;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    /// <summary>
    /// Controller for retrieving job status information.
    /// </summary>
    [ServiceFilter(typeof(AuditLoggingFilterAttribute))]
    [ServiceFilter(typeof(OperationOutcomeExceptionFilterAttribute))]
    [ValidateModelState]
    public class JobStatusController : Controller
    {
        private readonly IMediator _mediator;
        private readonly ILogger<JobStatusController> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="JobStatusController"/> class.
        /// </summary>
        /// <param name="mediator">The mediator.</param>
        /// <param name="logger">The logger.</param>
        public JobStatusController(IMediator mediator, ILogger<JobStatusController> logger)
        {
            _mediator = EnsureArg.IsNotNull(mediator, nameof(mediator));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        /// <summary>
        /// Gets a page of async job statuses (Export, Import, Reindex, BulkDelete, BulkUpdate).
        /// </summary>
        /// <param name="jobType">The operation name to filter by, or null for all supported job types.</param>
        /// <param name="continuationToken">The token from a previous page, or null for the first page.</param>
        /// <returns>A page of job status information and an optional next-page token.</returns>
        [HttpGet]
        [Route(KnownRoutes.JobStatus, Name = RouteNames.GetAllJobStatus)]
        [AuditEventType(AuditEventSubType.Read)]
        public async Task<IActionResult> GetAllJobStatus([FromQuery] string jobType = null, [FromQuery] string continuationToken = null)
        {
            var response = await _mediator.SendAsync(new GetAllJobStatusRequest(jobType, continuationToken), HttpContext.RequestAborted);

            return FhirResult.Create(_logger, response.Jobs.ToJobStatusResult(response.ContinuationToken));
        }
    }
}
