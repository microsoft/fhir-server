// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Api.Features.Security;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Messages.Operation;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    [ServiceFilter(typeof(AuditLoggingFilterAttribute))]
    [ServiceFilter(typeof(OperationOutcomeExceptionFilterAttribute))]
    [ServiceFilter(typeof(ValidateContentTypeFilterAttribute))]
    [ValidateResourceTypeFilter]
    [ValidateModelState]
    [Authorize(PolicyNames.FhirPolicy)]
    public class ValidateController : Controller
    {
        private readonly IMediator _mediator;

        public ValidateController(IMediator mediator)
        {
            _mediator = mediator;
        }

        [HttpPost]
        [Route(KnownRoutes.ResourceType + "$validate")]
        [AuditEventType(AuditEventSubType.Read)]
        [Authorize(PolicyNames.ReadPolicy)]
        public async Task<IActionResult> Validate([FromBody] Resource resource)
        {
            var response = await _mediator.Send<ValidateOperationResponse>(new ValidateOperationRequest(resource.ToResourceElement()));

            if (resource.ResourceType == ResourceType.Parameters)
            {
                var outcome = new OperationOutcome();
                outcome.Issue.Add(new OperationOutcome.IssueComponent
                {
                    Severity = OperationOutcome.IssueSeverity.Error,
                    Code = OperationOutcome.IssueType.NotSupported,
                    Diagnostics = "Not supported",
                });

                return FhirResult.Create(outcome.ToResourceElement());
            }

            return FhirResult.Create(new OperationOutcome
            {
                Issue = response.Issues.Select(x => x.ToPoco()).ToList(),
            }.ToResourceElement());
        }
    }
}
