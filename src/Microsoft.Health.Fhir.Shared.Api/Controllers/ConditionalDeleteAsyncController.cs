// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.ContentTypes;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Headers;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Api.Controllers;

[ServiceFilter(typeof(AuditLoggingFilterAttribute))]
[ServiceFilter(typeof(OperationOutcomeExceptionFilterAttribute))]
[ValidateModelState]
public class ConditionalDeleteAsyncController : Controller
{
    private readonly IMediator _mediator;
    private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor;

    public ConditionalDeleteAsyncController(
        IMediator mediator,
        RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor)
    {
        EnsureArg.IsNotNull(mediator, nameof(mediator));
        EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));

        _mediator = mediator;
        _fhirRequestContextAccessor = fhirRequestContextAccessor;
    }

    [HttpGet]
    [Route(KnownRoutes.ConditionalDeleteJobLocation, Name = RouteNames.ConditionalDeleteAsyncJobById)]
    [AuditEventType(AuditEventSubType.ConditionalDelete)]
    public async Task<IActionResult> GetStatusById(long idParameter)
    {
        var getResult =
            await _mediator.Send(new GetConditionalDeleteResourceAsyncRequest(idParameter), HttpContext.RequestAborted);

        // If the job is complete, we need to return 200 along with the completed data to the client.
        // Else we need to return 202 - Accepted.
        JobResult<Resource> result;
        if (getResult.IsCompleted)
        {
            result = JobResult<Resource>.Ok(getResult.JobResult);
            result.SetContentTypeHeader(KnownContentTypes.JsonContentType);
            result.StatusCode = getResult.JobStatus;
        }
        else
        {
            result = JobResult<Resource>.Accepted();
        }

        return result;
    }

    [HttpDelete]
    [Route(KnownRoutes.ConditionalDeleteJobLocation, Name = RouteNames.ConditionalDeleteAsyncJobById)]
    [AuditEventType(AuditEventSubType.ConditionalDelete)]
    public async Task<IActionResult> Cancel(long idParameter)
    {
        await _mediator.Send(new DeleteConditionalDeleteResourceAsyncRequest(idParameter), HttpContext.RequestAborted);
        return new JobResult<ExportJobResult>(HttpStatusCode.Accepted);
    }
}
