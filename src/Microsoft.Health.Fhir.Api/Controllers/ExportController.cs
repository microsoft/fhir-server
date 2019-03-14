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
        private readonly ILogger<ExportController> _logger;
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;
        private readonly ExportConfiguration _exportConfig;

        public ExportController(
            ILogger<ExportController> logger,
            IFhirRequestContextAccessor fhirRequestContextAccessor,
            IOptions<ExportConfiguration> exportConfig)
        {
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(exportConfig, nameof(exportConfig));

            _logger = logger;
            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _exportConfig = exportConfig.Value;
        }

        [HttpGet]
        [Route(KnownRoutes.Export)]
        [ValidateExportHeadersFilter]
        public IActionResult Export()
        {
            HttpStatusCode returnCode;
            OperationOutcome result;

            if (_exportConfig.Enabled)
            {
                result = GenerateOperationOutcome(
                    OperationOutcome.IssueSeverity.Error,
                    OperationOutcome.IssueType.NotSupported,
                    Resources.NotFoundException);

                returnCode = HttpStatusCode.NotImplemented;
            }
            else
            {
                throw new RequestNotValidException(string.Format(Resources.UnsupportedOperation, "Export"));
            }

            return FhirResult.Create(result, returnCode);
        }

        [HttpGet]
        [Route(KnownRoutes.ExportResourceType)]
        [ValidateExportHeadersFilter]
        public IActionResult ExportResourceType(string type)
        {
            // Export by ResourceType is supported only for Patient resource type.
            if (!string.Equals(type, ResourceType.Patient.ToString(), StringComparison.Ordinal))
            {
                throw new RequestNotValidException(Resources.UnsupportedResourceType);
            }

            // Currently we don't have any functionality. We are going to re-use the logic in Export()
            // to return the appropriate response code based on whether Export is enabled or not.
            return Export();
        }

        [HttpGet]
        [Route(KnownRoutes.ExportResourceTypeById)]
        [ValidateExportHeadersFilter]
        public IActionResult ExportResourceTypeById(string type, string id)
        {
            // Export by ResourceTypeId is supported only for Group resource type.
            if (!string.Equals(type, ResourceType.Group.ToString(), StringComparison.Ordinal) || string.IsNullOrEmpty(id))
            {
                throw new RequestNotValidException(Resources.UnsupportedResourceType);
            }

            // Currently we don't have any functionality. We are going to re-use the logic in Export()
            // to return the appropriate response code based on whether Export is enabled or not.
            return Export();
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
