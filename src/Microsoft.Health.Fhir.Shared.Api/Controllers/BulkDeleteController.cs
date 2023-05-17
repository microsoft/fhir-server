// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    [ServiceFilter(typeof(AuditLoggingFilterAttribute))]
    [ServiceFilter(typeof(OperationOutcomeExceptionFilterAttribute))]
    [ValidateModelState]
    public class BulkDeleteController : Controller
    {
        public BulkDeleteController() { }

        [HttpGet]
        [Route(KnownRoutes.BulkDelete)]
        [AuditEventType(AuditEventSubType.BulkDelete)]
        public async Task<IActionResult> BulkDelete() { }

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
