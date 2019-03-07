// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Net;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;

namespace Microsoft.Health.Fhir.Api.Controllers
{
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

        [Route(KnownRoutes.Export)]
        [ValidateOperationHeadersFilter]
        [AllowAnonymous]
        public IActionResult BulkExport()
        {
            HttpStatusCode returnCode;
            OperationOutcome.IssueType issueType;

            if (_operationsConfig.SupportsBulkExport)
            {
                if (ValidateHeaders())
                {
                    returnCode = HttpStatusCode.NotImplemented;
                    issueType = OperationOutcome.IssueType.NotSupported;
                }
                else
                {
                    returnCode = HttpStatusCode.BadRequest;
                    issueType = OperationOutcome.IssueType.Value;
                }
            }
            else
            {
                returnCode = HttpStatusCode.BadRequest;
                issueType = OperationOutcome.IssueType.Value;
            }

            var result = new OperationOutcome()
            {
                Id = _fhirRequestContextAccessor.FhirRequestContext.CorrelationId,
                Issue = new List<OperationOutcome.IssueComponent>
                {
                    new OperationOutcome.IssueComponent
                    {
                        Severity = OperationOutcome.IssueSeverity.Error,
                        Code = issueType,
                    },
                },
            };

            return FhirResult.Create(result, returnCode);
        }

        [HttpGet]
        [Route(KnownRoutes.ExportResourceType)]
        public IActionResult BulkExportPatient(string type)
        {
            if (!string.Equals(type, "Patient", StringComparison.Ordinal))
            {
                var result = new OperationOutcome()
                {
                    Id = _fhirRequestContextAccessor.FhirRequestContext.CorrelationId,
                    Issue = new List<OperationOutcome.IssueComponent>
                {
                    new OperationOutcome.IssueComponent
                    {
                        Severity = OperationOutcome.IssueSeverity.Error,
                        Code = OperationOutcome.IssueType.NotSupported,
                    },
                },
                };

                return FhirResult.Create(result, HttpStatusCode.BadRequest);
            }

            return BulkExport();
        }

        [HttpGet]
        [Route(KnownRoutes.ExportResourceTypeById)]
        public IActionResult BulkExportGroupById(string type, string id)
        {
            if (!string.Equals(type, "Group", StringComparison.Ordinal) || string.IsNullOrEmpty(id))
            {
                var result = new OperationOutcome()
                {
                    Id = _fhirRequestContextAccessor.FhirRequestContext.CorrelationId,
                    Issue = new List<OperationOutcome.IssueComponent>
                {
                    new OperationOutcome.IssueComponent
                    {
                        Severity = OperationOutcome.IssueSeverity.Error,
                        Code = OperationOutcome.IssueType.NotSupported,
                    },
                },
                };

                return FhirResult.Create(result, HttpStatusCode.BadRequest);
            }

            return BulkExport();
        }

        private bool ValidateHeaders()
        {
            // check whether accept header is present
            StringValues acceptHeaderValue;
            HttpContext.Request.Headers.TryGetValue(HttpRequestHeader.Accept.ToString(), out acceptHeaderValue);

            if (acceptHeaderValue.Count != 1)
            {
                return false;
            }

            if (acceptHeaderValue[0] != "application/fhir+json")
            {
                return false;
            }

            StringValues preferHeaderValue;
            HttpContext.Request.Headers.TryGetValue("Prefer", out preferHeaderValue);

            if (preferHeaderValue.Count != 1)
            {
                return false;
            }

            if (preferHeaderValue[0] != "respond-async")
            {
                return false;
            }

            return true;
        }
    }
}
