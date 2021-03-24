// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Headers;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Messages.Reset;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    [ServiceFilter(typeof(AuditLoggingFilterAttribute))]
    [ServiceFilter(typeof(OperationOutcomeExceptionFilterAttribute))]
    public class ResetController : Controller
    {
        /*
         * We are currently hardcoding the routing attribute to be specific to Export and
         * get forwarded to this controller. As we add more operations we would like to resolve
         * the routes in a more dynamic manner. One way would be to use a regex route constraint
         * - eg: "{operation:regex(^\\$([[a-zA-Z]]+))}" - and use the appropriate operation handler.
         * Another way would be to use the capability statement to dynamically determine what operations
         * are supported.
         * It would be easier to determine what pattern to follow once we have built support for a couple
         * of operations. Then we can refactor this controller accordingly.
         */

        private readonly IMediator _mediator;
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;
        private readonly IUrlResolver _urlResolver;
        private readonly ILogger<ResetController> _logger;

        public ResetController(
            IFhirRequestContextAccessor fhirRequestContextAccessor,
            IUrlResolver urlResolver,
            IMediator mediator,
            ILogger<ResetController> logger)
        {
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(urlResolver, nameof(urlResolver));
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _urlResolver = urlResolver;
            _mediator = mediator;
            _logger = logger;
        }

        [HttpPost]
        [Route(KnownRoutes.Reset)]
        [AuditEventType(AuditEventSubType.Reset)]
        public async Task<IActionResult> Reset()
        {
            CreateResetResponse response = await _mediator.ResetAsync(
                 _fhirRequestContextAccessor.FhirRequestContext.Uri,
                 HttpContext.RequestAborted);

            var resetResult = ResetResult.Accepted();
            resetResult.SetContentLocationHeader(_urlResolver, OperationsConstants.Reset, response.JobId);
            return resetResult;
        }

        [HttpGet]
        [Route(KnownRoutes.ResetJobLocation, Name = RouteNames.GetResetStatusById)]
        [AuditEventType(AuditEventSubType.Reset)]
        public async Task<IActionResult> GetResetStatusById(string idParameter)
        {
            var getResetResult = await _mediator.GetResetStatusAsync(
                _fhirRequestContextAccessor.FhirRequestContext.Uri,
                idParameter,
                HttpContext.RequestAborted);

            // If the job is complete, we need to return 200 along with the completed data to the client.
            // Else we need to return 202 - Accepted.
            ResetResult resetActionResult;
            if (getResetResult.StatusCode == HttpStatusCode.OK)
            {
                resetActionResult = ResetResult.Ok(getResetResult.JobResult);
                resetActionResult.SetContentTypeHeader(OperationsConstants.ResetContentTypeHeaderValue);
            }
            else
            {
                resetActionResult = ResetResult.Accepted();
            }

            return resetActionResult;
        }
    }
}
