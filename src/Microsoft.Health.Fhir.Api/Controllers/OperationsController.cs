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
        private IFhirRequestContextAccessor _fhirRequestContextAccessor;
        private OperationsConfiguration _operationsConfig;

        public OperationsController(
            IFhirRequestContextAccessor fhirRequestContextAccessor,
            IOptions<OperationsConfiguration> operationConfig)
        {
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(operationConfig, nameof(operationConfig));

            _fhirRequestContextAccessor = fhirRequestContextAccessor;
            _operationsConfig = operationConfig.Value;
        }

        [HttpGet]
        [Route(KnownRoutes.Export)]
        [ValidateOperationHeadersFilter]
        public IActionResult Export()
        {
            HttpStatusCode returnCode;
            OperationOutcome.IssueType issueType;
            string diagnosticInfo;

            if (_operationsConfig.SupportsBulkExport)
            {
                returnCode = HttpStatusCode.NotImplemented;
                issueType = OperationOutcome.IssueType.NotSupported;
                diagnosticInfo = "Export operation not supported";
            }
            else
            {
                returnCode = HttpStatusCode.BadRequest;
                issueType = OperationOutcome.IssueType.Value;
                diagnosticInfo = "Export operation disabled";
            }

            OperationOutcome result = GenerateOperationOutcome(
                OperationOutcome.IssueSeverity.Error,
                issueType,
                diagnosticInfo);

            return FhirResult.Create(result, returnCode);
        }

        [HttpGet]
        [Route(KnownRoutes.ExportResourceType)]
        [ValidateOperationHeadersFilter]
        public IActionResult ExportResource(string type)
        {
            // Export by ResourceType is supported only for Patient resource type.
            if (!string.Equals(type, "Patient", StringComparison.Ordinal))
            {
                OperationOutcome result = GenerateOperationOutcome(
                    OperationOutcome.IssueSeverity.Error,
                    OperationOutcome.IssueType.NotSupported,
                    $"{type} type not supported for Export by ResourceType operation");

                return FhirResult.Create(result, HttpStatusCode.BadRequest);
            }

            return Export();
        }

        [HttpGet]
        [Route(KnownRoutes.ExportResourceTypeById)]
        [ValidateOperationHeadersFilter]
        public IActionResult ExportResourceById(string type, string id)
        {
            // Export by ResourceTypeId is supported only for Group resource type.
            if (!string.Equals(type, "Group", StringComparison.Ordinal) || string.IsNullOrEmpty(id))
            {
                OperationOutcome result = GenerateOperationOutcome(
                    OperationOutcome.IssueSeverity.Error,
                    OperationOutcome.IssueType.NotSupported,
                    $"{type} type not supported for Export by ResourceTypeId operation");

                return FhirResult.Create(result, HttpStatusCode.BadRequest);
            }

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
