// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Headers;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Api.Features.Security;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Messages.Export;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    [ServiceFilter(typeof(AuditLoggingFilterAttribute))]
    [ServiceFilter(typeof(OperationOutcomeExceptionFilterAttribute))]
    [Authorize(PolicyNames.FhirPolicy)]
    [Authorize(PolicyNames.ExportPolicy)]
    public class ExportController : Controller
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
        private readonly ExportJobConfiguration _exportConfig;
        private readonly ILogger<ExportController> _logger;

        public ExportController(
            IMediator mediator,
            IFhirRequestContextAccessor fhirRequestContextAccessor,
            IUrlResolver urlResolver,
            IOptions<OperationsConfiguration> operationsConfig,
            ILogger<ExportController> logger)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(urlResolver, nameof(urlResolver));
            EnsureArg.IsNotNull(operationsConfig?.Value?.Export, nameof(operationsConfig));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _mediator = mediator;
            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _urlResolver = urlResolver;
            _exportConfig = operationsConfig.Value.Export;
            _logger = logger;
        }

        [HttpGet]
        [Route(KnownRoutes.Export)]
        [ServiceFilter(typeof(ValidateExportRequestFilterAttribute))]
        [AuditEventType(AuditEventSubType.Export)]
        public async Task<IActionResult> Export(
            [FromQuery(Name = KnownQueryParameterNames.DestinationType)] string destinationType,
            [FromQuery(Name = KnownQueryParameterNames.DestinationConnectionSettings)] string destinationConnectionString)
        {
            if (!_exportConfig.Enabled)
            {
                throw new RequestNotValidException(string.Format(Resources.UnsupportedOperation, OperationsConstants.Export));
            }

            CreateExportResponse response = await _mediator.ExportAsync(_fhirRequestContextAccessor.FhirRequestContext.Uri, destinationType, destinationConnectionString, HttpContext.RequestAborted);

            var exportResult = ExportResult.Accepted();
            exportResult.SetContentLocationHeader(_urlResolver, OperationsConstants.Export, response.JobId);

            return exportResult;
        }

        [HttpGet]
        [Route(KnownRoutes.ExportResourceType)]
        [ServiceFilter(typeof(ValidateExportRequestFilterAttribute))]
        [AuditEventType(AuditEventSubType.Export)]
        public IActionResult ExportResourceType(string typeParameter)
        {
            // Export by ResourceType is supported only for Patient resource type.
            if (!string.Equals(typeParameter, ResourceType.Patient.ToString(), StringComparison.Ordinal))
            {
                throw new RequestNotValidException(string.Format(Resources.UnsupportedResourceType, typeParameter));
            }

            return CheckIfExportIsEnabledAndRespond();
        }

        [HttpGet]
        [Route(KnownRoutes.ExportResourceTypeById)]
        [ServiceFilter(typeof(ValidateExportRequestFilterAttribute))]
        [AuditEventType(AuditEventSubType.Export)]
        public IActionResult ExportResourceTypeById(string typeParameter, string idParameter)
        {
            // Export by ResourceTypeId is supported only for Group resource type.
            if (!string.Equals(typeParameter, ResourceType.Group.ToString(), StringComparison.Ordinal) || string.IsNullOrEmpty(idParameter))
            {
                throw new RequestNotValidException(string.Format(Resources.UnsupportedResourceType, typeParameter));
            }

            return CheckIfExportIsEnabledAndRespond();
        }

        [HttpGet]
        [Route(KnownRoutes.ExportJobLocation, Name = RouteNames.GetExportStatusById)]
        [AuditEventType(AuditEventSubType.Export)]
        public async Task<IActionResult> GetExportStatusById(string idParameter)
        {
            var getExportResult = await _mediator.GetExportStatusAsync(
                _fhirRequestContextAccessor.FhirRequestContext.Uri,
                idParameter,
                HttpContext.RequestAborted);

            // If the job is complete, we need to return 200 along the completed data to the client.
            // Else we need to return 202.
            ExportResult exportActionResult;
            if (getExportResult.StatusCode == HttpStatusCode.OK)
            {
                exportActionResult = ExportResult.Ok(getExportResult.JobResult);
                exportActionResult.SetContentTypeHeader(OperationsConstants.ExportContentTypeHeaderValue);
            }
            else
            {
                exportActionResult = ExportResult.Accepted();
            }

            return exportActionResult;
        }

        [HttpDelete]
        [Route(KnownRoutes.ExportJobLocation, Name = RouteNames.CancelExport)]
        [AuditEventType(AuditEventSubType.Export)]
        public async Task<IActionResult> CancelExport(string idParameter)
        {
            CancelExportResponse response = await _mediator.CancelExportAsync(idParameter, HttpContext.RequestAborted);

            return new ExportResult(response.StatusCode);
        }

        /// <summary>
        /// Currently we don't have any export functionality. We will send the appropriate
        /// response based on whether export is enabled or not.
        /// </summary>
        private FhirResult CheckIfExportIsEnabledAndRespond()
        {
            if (!_exportConfig.Enabled)
            {
                throw new RequestNotValidException(string.Format(Resources.UnsupportedOperation, OperationsConstants.Export));
            }

            throw new OperationNotImplementedException(string.Format(Resources.OperationNotImplemented, OperationsConstants.Export));
        }
    }
}
