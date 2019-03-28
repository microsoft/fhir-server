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
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Headers;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Api.Features.Security;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Messages.Export;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    [ServiceFilter(typeof(AuditLoggingFilterAttribute), Order = -1)]
    [ServiceFilter(typeof(OperationOutcomeExceptionFilterAttribute))]
    [Authorize(PolicyNames.FhirPolicy)]
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
        private readonly ExportConfiguration _exportConfig;
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
        [ValidateExportHeadersFilter]
        [AuditEventType(AuditEventSubType.Export)]
        public async Task<IActionResult> Export()
        {
            if (!_exportConfig.Enabled)
            {
                throw new RequestNotValidException(string.Format(Resources.UnsupportedOperation, "Export"));
            }

            CreateExportResponse response = await _mediator.ExportAsync(_fhirRequestContextAccessor.FhirRequestContext.Uri);

            HttpStatusCode responseCode;
            if (response.JobStatus.Equals(JobCreationStatus.Failed))
            {
                responseCode = HttpStatusCode.InternalServerError;
                throw new MicrosoftHealthException(Resources.GeneralInternalError);
            }
            else
            {
                responseCode = HttpStatusCode.Accepted;
            }

            var fhirResult = new FhirResult()
            {
                StatusCode = responseCode,
            };
            fhirResult.SetContentLocationHeader(_urlResolver, OperationsConstants.Export, response.Id);

            return fhirResult;
        }

        [HttpGet]
        [Route(KnownRoutes.ExportResourceType)]
        [ValidateExportHeadersFilter]
        [AuditEventType(AuditEventSubType.Export)]
        public IActionResult ExportResourceType(string type)
        {
            // Export by ResourceType is supported only for Patient resource type.
            if (!string.Equals(type, ResourceType.Patient.ToString(), StringComparison.Ordinal))
            {
                throw new RequestNotValidException(string.Format(Resources.UnsupportedResourceType, type));
            }

            return CheckIfExportIsEnabledAndRespond();
        }

        [HttpGet]
        [Route(KnownRoutes.ExportResourceTypeById)]
        [ValidateExportHeadersFilter]
        [AuditEventType(AuditEventSubType.Export)]
        public IActionResult ExportResourceTypeById(string type, string id)
        {
            // Export by ResourceTypeId is supported only for Group resource type.
            if (!string.Equals(type, ResourceType.Group.ToString(), StringComparison.Ordinal) || string.IsNullOrEmpty(id))
            {
                throw new RequestNotValidException(string.Format(Resources.UnsupportedResourceType, type));
            }

            return CheckIfExportIsEnabledAndRespond();
        }

        [HttpGet]
        [Route(KnownRoutes.ExportStatusById, Name = RouteNames.GetExportStatusById)]
        public IActionResult GetExportStatusById(string id)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Currently we don't have any export functionality. We will send the appropriate
        /// response based on whether export is enabled or not.
        /// </summary>
        private FhirResult CheckIfExportIsEnabledAndRespond()
        {
            if (!_exportConfig.Enabled)
            {
                throw new RequestNotValidException(string.Format(Resources.UnsupportedOperation, "Export"));
            }

            throw new OperationNotImplementedException(string.Format(Resources.OperationNotImplemented, "Export"));
        }
    }
}
