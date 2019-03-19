// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Net;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Context;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    [ServiceFilter(typeof(OperationOutcomeExceptionFilterAttribute))]
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

        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;
        private readonly ExportConfiguration _exportConfig;
        private readonly ILogger<ExportController> _logger;

        public ExportController(
            IFhirRequestContextAccessor fhirRequestContextAccessor,
            IOptions<ExportConfiguration> exportConfig,
            ILogger<ExportController> logger)
        {
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(exportConfig?.Value, nameof(exportConfig));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _exportConfig = exportConfig.Value;
            _logger = logger;
        }

        [HttpGet]
        [Route(KnownRoutes.Export)]
        [ValidateExportHeadersFilter]
        public IActionResult Export()
        {
            return CheckIfExportIsEnabledAndRespond();
        }

        [HttpGet]
        [Route(KnownRoutes.ExportResourceType)]
        [ValidateExportHeadersFilter]
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
        public IActionResult ExportResourceTypeById(string type, string id)
        {
            // Export by ResourceTypeId is supported only for Group resource type.
            if (!string.Equals(type, ResourceType.Group.ToString(), StringComparison.Ordinal) || string.IsNullOrEmpty(id))
            {
                throw new RequestNotValidException(string.Format(Resources.UnsupportedResourceType, type));
            }

            return CheckIfExportIsEnabledAndRespond();
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

            OperationOutcome result = GenerateOperationOutcome(
                OperationOutcome.IssueSeverity.Error,
                OperationOutcome.IssueType.NotSupported,
                Resources.NotFoundException);

            return FhirResult.Create(result, HttpStatusCode.NotImplemented);
        }

        private OperationOutcome GenerateOperationOutcome(
            OperationOutcome.IssueSeverity issueSeverity,
            OperationOutcome.IssueType issueType,
            string diagnosticInfo)
        {
            return new OperationOutcome()
            {
                Id = _fhirRequestContextAccessor.FhirRequestContext.CorrelationId,
                Issue = new List<OperationOutcome.IssueComponent>
                {
                    new OperationOutcome.IssueComponent
                    {
                        Severity = issueSeverity,
                        Code = issueType,
                        Diagnostics = diagnosticInfo,
                    },
                },
            };
        }
    }
}
