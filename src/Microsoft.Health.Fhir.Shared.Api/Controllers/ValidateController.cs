// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Messages.Operation;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    [ServiceFilter(typeof(AuditLoggingFilterAttribute))]
    [ServiceFilter(typeof(OperationOutcomeExceptionFilterAttribute))]
    [ServiceFilter(typeof(ValidateContentTypeFilterAttribute))]
    [ServiceFilter(typeof(ValidationQueryFilterAndParameterParserAttribute))]
    [ValidateResourceTypeFilter(true)]
    [ValidateModelState]
    public class ValidateController : Controller
    {
        private readonly IMediator _mediator;

        public ValidateController(IMediator mediator)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            _mediator = mediator;
        }

        [HttpPost]
        [Route(KnownRoutes.ValidateResourceType)]
        [AuditEventType(AuditEventSubType.Read)]
        public async Task<IActionResult> Validate([FromBody] Resource resource)
        {
            return await RunValidationAsync(resource);
        }

        [HttpPost]
        [Route(KnownRoutes.ValidateResourceTypeById)]
        [AuditEventType(AuditEventSubType.Read)]
        [ValidateResourceIdFilter(true)]
        public async Task<IActionResult> ValidateById([FromBody] Resource resource)
        {
            return await RunValidationAsync(resource);
        }

        private async Task<IActionResult> RunValidationAsync(Resource resource)
        {
            var response = await _mediator.Send<ValidateOperationResponse>(new ValidateOperationRequest(resource.ToResourceElement()));

            return FhirResult.Create(new OperationOutcome
            {
                Issue = response.Issues.Select(x => x.ToPoco()).ToList(),
            }.ToResourceElement());
        }
    }
}
