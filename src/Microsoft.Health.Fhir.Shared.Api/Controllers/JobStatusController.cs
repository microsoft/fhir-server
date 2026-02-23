// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.AspNetCore.Mvc;
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

        /// <summary>
        /// Initializes a new instance of the <see cref="JobStatusController"/> class.
        /// </summary>
        /// <param name="mediator">The mediator.</param>
        public JobStatusController(IMediator mediator)
        {
            _mediator = EnsureArg.IsNotNull(mediator, nameof(mediator));
        }

        /// <summary>
        /// Gets the status of all async jobs (Export, Import, Reindex, BulkDelete, BulkUpdate).
        /// </summary>
        /// <returns>A list of job status information.</returns>
        [HttpGet]
        [Route(KnownRoutes.JobStatus, Name = RouteNames.GetAllJobStatus)]
        [AuditEventType(AuditEventSubType.Read)]
        public async Task<IActionResult> GetAllJobStatus()
        {
            var response = await _mediator.Send(new GetAllJobStatusRequest(), HttpContext.RequestAborted);

            return FhirResult.Create(response.Jobs.ToJobStatusResult());
        }
    }
}
