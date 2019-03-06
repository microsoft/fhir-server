// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System.Collections.Generic;
using System.Net;
using EnsureThat;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
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

        [Route("$export")]
        [AllowAnonymous]
        public IActionResult BulkExport()
        {
            HttpStatusCode returnCode;
            OperationOutcome.IssueType issueType;

            if (_operationsConfig.SupportsBulkExport)
            {
                returnCode = HttpStatusCode.NotImplemented;
                issueType = OperationOutcome.IssueType.NotSupported;
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
    }
}
