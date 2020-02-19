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
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Messages.Operation;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    [ServiceFilter(typeof(AuditLoggingFilterAttribute))]
    [ServiceFilter(typeof(OperationOutcomeExceptionFilterAttribute))]
    [ServiceFilter(typeof(ValidateContentTypeFilterAttribute))]
    [ValidateResourceTypeFilter]
    [ValidateModelState]
    public class ValidateController : Controller
    {
        private readonly IMediator _mediator;
        private readonly FeatureConfiguration _features;

        public ValidateController(IMediator mediator, IOptions<FeatureConfiguration> features)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(features, nameof(features));
            EnsureArg.IsNotNull(features.Value, nameof(features));

            _mediator = mediator;
            _features = features.Value;
        }

        [HttpPost]
        [Route(KnownRoutes.ValidateResourceType)]
        [AuditEventType(AuditEventSubType.Read)]
        public async Task<IActionResult> Validate([FromBody] Resource resource, [FromQuery(Name = KnownQueryParameterNames.Profile)] string profile, [FromQuery(Name = KnownQueryParameterNames.Mode)] string mode)
        {
            if (!_features.SupportsValidate)
            {
                throw new OperationNotImplementedException(Resources.ValidationNotSupported);
            }

            if (resource.ResourceType == ResourceType.Parameters)
            {
                throw new OperationNotImplementedException(Resources.ValidateWithParametersNotSupported);
            }

            if (!string.IsNullOrEmpty(profile))
            {
                throw new OperationNotImplementedException(Resources.ValidateWithProfileNotSupported);
            }

            if (!string.IsNullOrEmpty(mode))
            {
                throw new OperationNotImplementedException(Resources.ValidationModesNotSupported);
            }

            var response = await _mediator.Send<ValidateOperationResponse>(new ValidateOperationRequest(resource.ToResourceElement()));

            return FhirResult.Create(new OperationOutcome
            {
                Issue = response.Issues.Select(x => x.ToPoco()).ToList(),
            }.ToResourceElement());
        }

        [HttpPost]
        [Route(KnownRoutes.ValidateResourceTypeById)]
        [AuditEventType(AuditEventSubType.Read)]
        [ValidateResourceIdFilter]
        public async Task<IActionResult> ValidateById([FromBody] Resource resource, [FromQuery(Name = KnownQueryParameterNames.Profile)] string profile, [FromQuery(Name = KnownQueryParameterNames.Mode)] string mode)
        {
            return await Validate(resource, profile, mode);
        }
    }
}
