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
using Microsoft.Health.Fhir.Core.Features.Context;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    [ServiceFilter(typeof(OperationOutcomeExceptionFilterAttribute))]
    public class OperationsController : Controller
    {
        private readonly ILogger<OperationsController> _logger;
        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor;
        private readonly OperationsConfiguration _operationsConfig;

        public OperationsController(
            ILogger<OperationsController> logger,
            IFhirRequestContextAccessor fhirRequestContextAccessor,
            IOptions<OperationsConfiguration> operationConfig)
        {
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(operationConfig, nameof(operationConfig));

            _logger = logger;
            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _operationsConfig = operationConfig.Value;
        }

        [HttpGet]
        [Route(KnownRoutes.Export)]
        [ValidateOperationHeadersFilter]
        public IActionResult Export()
        {
            HttpStatusCode returnCode;
            OperationOutcome result;

            if (_operationsConfig.SupportsExport)
            {
                result = GenerateOperationOutcome(
                    OperationOutcome.IssueSeverity.Error,
                    OperationOutcome.IssueType.NotSupported,
                    "Export operation not supported");
                returnCode = HttpStatusCode.NotImplemented;
            }
            else
            {
                result = GenerateOperationOutcome(
                    OperationOutcome.IssueSeverity.Error,
                    OperationOutcome.IssueType.Value,
                    "Export operation disabled");
                returnCode = HttpStatusCode.BadRequest;
            }

            return FhirResult.Create(result, returnCode);
        }

        [HttpGet]
        [Route(KnownRoutes.ExportResourceType)]
        [ValidateOperationHeadersFilter]
        public IActionResult ExportResourceType(string type)
        {
            // Export by ResourceType is supported only for Patient resource type.
            if (!string.Equals(type, ResourceType.Patient.ToString(), StringComparison.Ordinal))
            {
                OperationOutcome result = GenerateOperationOutcome(
                    OperationOutcome.IssueSeverity.Error,
                    OperationOutcome.IssueType.NotSupported,
                    $"{type} type not supported for Export by ResourceType operation");

                return FhirResult.Create(result, HttpStatusCode.BadRequest);
            }

            // Currently we don't have any functionality. We are going to re-use the logic in Export()
            // to return the appropriate response code based on the value of SupportsExport.
            return Export();
        }

        [HttpGet]
        [Route(KnownRoutes.ExportResourceTypeById)]
        [ValidateOperationHeadersFilter]
        public IActionResult ExportResourceTypeById(string type, string id)
        {
            // Export by ResourceTypeId is supported only for Group resource type.
            if (!string.Equals(type, ResourceType.Group.ToString(), StringComparison.Ordinal) || string.IsNullOrEmpty(id))
            {
                OperationOutcome result = GenerateOperationOutcome(
                    OperationOutcome.IssueSeverity.Error,
                    OperationOutcome.IssueType.NotSupported,
                    $"{type} type not supported for Export by ResourceTypeId operation");

                return FhirResult.Create(result, HttpStatusCode.BadRequest);
            }

            // Currently we don't have any functionality. We are going to re-use the logic in Export()
            // to return the appropriate response code based on the value of SupportsExport.
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
