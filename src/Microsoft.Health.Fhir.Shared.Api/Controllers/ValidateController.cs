// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EnsureThat;
using FluentValidation.Results;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Configs;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Api.Features.Security;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Messages.Operation;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    [ServiceFilter(typeof(AuditLoggingFilterAttribute))]
    [ServiceFilter(typeof(OperationOutcomeExceptionFilterAttribute))]
    [ServiceFilter(typeof(ValidateContentTypeFilterAttribute))]
    [ValidateModelState]
    [Authorize(PolicyNames.FhirPolicy)]
    public class ValidateController : Controller
    {
        private readonly IMediator _mediator;
        private readonly FeatureConfiguration _features;

        public ValidateController(IMediator mediator, IOptions<FeatureConfiguration> features)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(features, nameof(features));

            _mediator = mediator;
            _features = features.Value;
        }

        [HttpPost]
        [Route(KnownRoutes.ValidateResourceType)]
        [AuditEventType(AuditEventSubType.Read)]
        [Authorize(PolicyNames.ReadPolicy)]
        public async Task<IActionResult> Validate([FromBody] Resource resource, [FromQuery(Name = KnownQueryParameterNames.Profile)] string profile, [FromQuery(Name = KnownQueryParameterNames.Mode)] string mode, string typeParameter)
        {
            if (!_features.SupportsValidate)
            {
                throw new OperationNotImplementedException(Resources.ValidationNotSupported);
            }

            if (profile != null)
            {
                throw new OperationNotImplementedException(Resources.ValidateWithProfileNotSupported);
            }

            /*
             * In order to allow modes (create, update, and delete) I first need to understand what checks are done before performing those actions.
             * For create it seems to go through the FhirDataStore classes (ex: CosmosFhirDataStore), although what checks are run in addition to normal validation are unclear
             */
            if (mode != null)
            {
                throw new OperationNotImplementedException(Resources.ValidationModesNotSupported);
            }

            if (resource.ResourceType == ResourceType.Parameters)
            {
                resource = ParseParameters((Parameters)resource, ref profile, ref mode);
            }

            if (mode != null && !mode.Equals("create", System.StringComparison.OrdinalIgnoreCase))
            {
                throw new BadRequestException(Resources.ValidationForUpdateAndDeleteNotSupported);
            }

            ValidateType(resource, typeParameter);

            var response = await _mediator.Send<ValidateOperationResponse>(new ValidateOperationRequest(resource.ToResourceElement()));

            return FhirResult.Create(new OperationOutcome
            {
                Issue = response.Issues.Select(x => x.ToPoco()).ToList(),
            }.ToResourceElement());
        }

        [HttpPost]
        [Route(KnownRoutes.ValidateResourceTypeById)]
        [AuditEventType(AuditEventSubType.Read)]
        [Authorize(PolicyNames.ReadPolicy)]
        public async Task<IActionResult> ValidateById([FromBody] Resource resource, [FromQuery(Name = KnownQueryParameterNames.Profile)] string profile, [FromQuery(Name = KnownQueryParameterNames.Mode)] string mode, string typeParameter, string idParameter)
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

            ValidateType(resource, typeParameter);

            if (resource.Id == null)
            {
                throw new ResourceNotValidException(new List<ValidationFailure>
                    {
                        new ValidationFailure(nameof(Base.TypeName), Resources.ResourceIdRequired),
                    });
            }

            if (!resource.Id.Equals(idParameter, System.StringComparison.OrdinalIgnoreCase))
            {
                throw new ResourceNotValidException(new List<ValidationFailure>
                    {
                        new ValidationFailure(nameof(Base.TypeName), Resources.UrlResourceIdMismatch),
                    });
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

        private static void ValidateType(Resource resource, string expectedType)
        {
            if (!resource.TypeName.Equals(expectedType, System.StringComparison.OrdinalIgnoreCase))
            {
                throw new ResourceNotValidException(new List<ValidationFailure>
                    {
                        new ValidationFailure(nameof(Base.TypeName), Resources.ResourceTypeMismatch),
                    });
            }
        }
    }
}
