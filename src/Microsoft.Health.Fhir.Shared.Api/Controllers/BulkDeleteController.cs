// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Fhir.Api.Extensions;
using MediatR;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.Exceptions;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    [ServiceFilter(typeof(AuditLoggingFilterAttribute))]
    [ServiceFilter(typeof(OperationOutcomeExceptionFilterAttribute))]
    [ValidateModelState]
    public class BulkDeleteController : Controller
    {
        private readonly IMediator _mediator;

        public BulkDeleteController(IMediator mediator)
        {
            _mediator = mediator;
        }

        [HttpGet]
        [Route(KnownRoutes.BulkDelete)]
        [AuditEventType(AuditEventSubType.BulkDelete)]
        public async Task<IActionResult> BulkDelete(string typeParameter, [FromQuery] bool hardDelete, [FromQuery] bool purgeHistory)
        {
            if (!hardDelete && purgeHistory)
            {
                throw new RequestNotValidException(Resources.NoSoftPurge);
            }

            IReadOnlyList<Tuple<string, string>> searchParameters = Request.GetQueriesForSearch();
            var deleteOperation = hardDelete ? (purgeHistory ? DeleteOperation.PurgeHistory : DeleteOperation.HardDelete) : DeleteOperation.SoftDelete;
            var result = await _mediator.BulkDeleteAsync(deleteOperation, null, (IList<Tuple<string, string>>)searchParameters, HttpContext.RequestAborted);

        }

        [HttpGet]
        [Route(KnownRoutes.BulkDeleteResourceType)]
        [AuditEventType(AuditEventSubType.BulkDelete)]
        public async Task<IActionResult> BulkDeleteByResourceType() { }

        [HttpGet]
        [Route(KnownRoutes.BulkDeleteJobLocation, Name = RouteNames.GetBulkDeleteStatusById)]
        [AuditEventType(AuditEventSubType.Export)]
        public async Task<IActionResult> GetBulkDeleteStatusById(string idParameter) { }

        [HttpDelete]
        [Route(KnownRoutes.BulkDeleteJobLocation, Name = RouteNames.CancelBulkDelete)]
        [AuditEventType(AuditEventSubType.Export)]
        public async Task<IActionResult> CancelBulkDelete(string idParameter) { }
    }
}
