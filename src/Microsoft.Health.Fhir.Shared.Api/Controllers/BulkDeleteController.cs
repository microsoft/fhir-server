// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Extensions;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Headers;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete.Mediator;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    [ServiceFilter(typeof(AuditLoggingFilterAttribute))]
    [ServiceFilter(typeof(OperationOutcomeExceptionFilterAttribute))]
    [ValidateModelState]
    public class BulkDeleteController : Controller
    {
        private readonly IMediator _mediator;
        private readonly IUrlResolver _urlResolver;

        public BulkDeleteController(
            IMediator mediator,
            IUrlResolver urlResolver)
        {
            _mediator = EnsureArg.IsNotNull(mediator, nameof(mediator));
            _urlResolver = EnsureArg.IsNotNull(urlResolver, nameof(urlResolver));
        }

        [HttpDelete]
        [Route(KnownRoutes.BulkDelete)]
        [ServiceFilter(typeof(ValidateAsyncRequestFilterAttribute))]
        [AuditEventType(AuditEventSubType.BulkDelete)]
        public async Task<IActionResult> BulkDelete([FromQuery] bool hardDelete, [FromQuery] bool purgeHistory)
        {
            return await SendDeleteRequest(null, hardDelete, purgeHistory);
        }

        [HttpDelete]
        [Route(KnownRoutes.BulkDeleteResourceType)]
        [ServiceFilter(typeof(ValidateReindexRequestFilterAttribute))]
        [AuditEventType(AuditEventSubType.BulkDelete)]
        public async Task<IActionResult> BulkDeleteByResourceType(string typeParameter, [FromQuery] bool hardDelete, [FromQuery] bool purgeHistory)
        {
            return await SendDeleteRequest(typeParameter, hardDelete, purgeHistory);
        }

        [HttpGet]
        [Route(KnownRoutes.BulkDeleteJobLocation, Name = RouteNames.GetBulkDeleteStatusById)]
        [AuditEventType(AuditEventSubType.Export)]
        public async Task<IActionResult> GetBulkDeleteStatusById(long idParameter)
        {
            var result = await _mediator.GetBulkDeleteStatusAsync(idParameter, HttpContext.RequestAborted);
            return JobResult.FromResults(result.Results, result.Issues, result.HttpStatusCode, "Resources Deleted");
        }

        [HttpDelete]
        [Route(KnownRoutes.BulkDeleteJobLocation, Name = RouteNames.CancelBulkDelete)]
        [AuditEventType(AuditEventSubType.Export)]
        public async Task<IActionResult> CancelBulkDelete(long idParameter)
        {
            var result = await _mediator.CancelBulkDeleteAsync(idParameter, HttpContext.RequestAborted);
            return new JobResult(result.StatusCode);
        }

        private async Task<IActionResult> SendDeleteRequest(string typeParameter, bool hardDelete, bool purgeHistory)
        {
            if (!hardDelete && purgeHistory)
            {
                throw new RequestNotValidException(Resources.NoSoftPurge);
            }

            IList<Tuple<string, string>> searchParameters = Request.GetQueriesForSearch().ToList();
            searchParameters = searchParameters.Where(
                param => !param.Item1.Equals(KnownQueryParameterNames.HardDelete, StringComparison.OrdinalIgnoreCase)
                && !param.Item1.Equals(KnownQueryParameterNames.PurgeHistory, StringComparison.OrdinalIgnoreCase)).ToList();

            var deleteOperation = hardDelete ? (purgeHistory ? DeleteOperation.PurgeHistory : DeleteOperation.HardDelete) : DeleteOperation.SoftDelete;
            var result = await _mediator.BulkDeleteAsync(deleteOperation, typeParameter, searchParameters, HttpContext.RequestAborted);

            var response = JobResult.Accepted();
            response.SetContentLocationHeader(_urlResolver, OperationsConstants.BulkDelete, result.Id.ToString());
            return response;
        }
    }
}
