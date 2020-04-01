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
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Operation;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    [ServiceFilter(typeof(AuditLoggingFilterAttribute))]
    [ServiceFilter(typeof(OperationOutcomeExceptionFilterAttribute))]
    [ServiceFilter(typeof(ValidateContentTypeFilterAttribute))]
    [ValidationModeFilter]
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
        [Authorize(PolicyNames.ReadPolicy)]
        public async Task<IActionResult> Validate([FromBody] Resource resource, [FromQuery(Name = KnownQueryParameterNames.Profile)] string profile, [FromQuery(Name = KnownQueryParameterNames.Mode)] string mode, string typeParameter)
        {
            return await RunValidationAsync(resource, profile, mode, typeParameter);
        }

        [HttpPost]
        [Route(KnownRoutes.ValidateResourceTypeById)]
        [AuditEventType(AuditEventSubType.Read)]
        [Authorize(PolicyNames.ReadPolicy)]
        public async Task<IActionResult> ValidateById([FromBody] Resource resource, [FromQuery(Name = KnownQueryParameterNames.Profile)] string profile, [FromQuery(Name = KnownQueryParameterNames.Mode)] string mode, string typeParameter, string idParameter)
        {
            return await RunValidationAsync(resource, profile, mode, typeParameter, idParameter, true);
        }

        private async Task<IActionResult> RunValidationAsync(Resource resource, string profile, string mode, string typeParameter, string idParameter = null, bool idMode = false)
        {
            if (!_features.SupportsValidate)
            {
                throw new OperationNotImplementedException(Resources.ValidationNotSupported);
            }

            if (profile != null)
            {
                throw new OperationNotImplementedException(Resources.ValidateWithProfileNotSupported);
            }

            if (resource.ResourceType == ResourceType.Parameters)
            {
                resource = ParseParameters((Parameters)resource, ref profile, ref mode);
            }

            // This is the same as the filter that is applied in the ValidationModeFilter.
            // It is needed here to cover the case of the mode being passed as part of a Parameters resource.
            // It is needed as a filter attribute so that it can perform the filter before the ValidateModelState filter returns an error if the user passed an invalid resource.
            // This is needed because if a user requests a delete validation it doesn't matter what resource they pass, so the delete validation should run regardless of if the resource is valid.
            ValidationModeFilterAttribute.ParseMode(mode, idMode);

            ValidateResourceTypeFilterAttribute.ValidateType(resource, typeParameter);

            if (idMode)
            {
                ValidateResourceIdFilterAttribute.ValidateId(resource, idParameter);
            }

            var response = await _mediator.Send<ValidateOperationResponse>(new ValidateOperationRequest(resource.ToResourceElement()));

            return FhirResult.Create(new OperationOutcome
            {
                Issue = response.Issues.Select(x => x.ToPoco()).ToList(),
            }.ToResourceElement());
        }

        private static Resource ParseParameters(Parameters resource, ref string profile, ref string mode)
        {
            var paramMode = resource.Parameter.Find(param => param.Name.Equals("mode", System.StringComparison.OrdinalIgnoreCase));
            if (paramMode != null && mode != null)
            {
                throw new BadRequestException(Resources.MultipleModesProvided);
            }
            else if (paramMode != null && mode == null)
            {
                mode = paramMode.Value.ToString();
            }

            var paramProfile = resource.Parameter.Find(param => param.Name.Equals("profile", System.StringComparison.OrdinalIgnoreCase));
            if (paramProfile != null && profile != null)
            {
                throw new BadRequestException(Resources.MultipleProfilesProvided);
            }
            else if (paramProfile != null && profile == null)
            {
                profile = paramProfile.Value.ToString();
            }

            return resource.Parameter.Find(param => param.Name.Equals("resource", System.StringComparison.OrdinalIgnoreCase)).Resource;
        }
    }
}
