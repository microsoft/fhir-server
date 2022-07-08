// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Operation;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;
using static Hl7.Fhir.Model.OperationOutcome;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    [ServiceFilter(typeof(AuditLoggingFilterAttribute))]
    [ServiceFilter(typeof(OperationOutcomeExceptionFilterAttribute))]
    [ServiceFilter(typeof(ValidateFormatParametersAttribute))]
    [ValidateResourceTypeFilter(true)]
    [ValidateModelState]
    public class ValidateController : Controller
    {
        private readonly IMediator _mediator;
        private readonly ResourceDeserializer _resourceDeserializer;

        public ValidateController(IMediator mediator, ResourceDeserializer resourceDeserializer)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            _mediator = mediator;
            _resourceDeserializer = resourceDeserializer;
        }

        [HttpPost]
        [Route(KnownRoutes.ValidateResourceType)]
        [AuditEventType(AuditEventSubType.Validate)]
        public async Task<IActionResult> Validate([FromBody] Resource resource, [FromQuery(Name = "profile")] string profile)
        {
            ProcessResource(ref resource, ref profile);

            Uri profileUri = GetProfile(profile);

            return await RunValidationAsync(resource.ToResourceElement(), profileUri);
        }

        private static void ProcessResource(ref Resource resource, ref string profile)
        {
            if (resource.TypeName == KnownResourceTypes.Parameters)
            {
                var parameterResource = (Parameters)resource;
                var profileFromParameters = parameterResource.Parameter.Find(param => param.Name.Equals("profile", StringComparison.OrdinalIgnoreCase));
                if (profileFromParameters != null)
                {
                    if (profile != null)
                    {
                        throw new BadRequestException(Resources.MultipleProfilesProvided);
                    }

                    if (profileFromParameters.Value != null)
                    {
                        profile = profileFromParameters.Value.ToString();
                    }
                }

                resource = parameterResource.Parameter.Find(param => param.Name.Equals("resource", StringComparison.OrdinalIgnoreCase)).Resource;
            }
        }

        [HttpGet]
        [Route(KnownRoutes.ValidateResourceTypeById)]
        [AuditEventType(AuditEventSubType.Validate)]
        public async Task<IActionResult> ValidateById([FromRoute] string typeParameter, [FromRoute] string idParameter, [FromQuery] string profile)
        {
            Uri profileUri = GetProfile(profile);

            // Read resource from storage.
            RawResourceElement response = await _mediator.GetResourceAsync(new ResourceKey(typeParameter, idParameter), HttpContext.RequestAborted);

            // Convert it to fhir object.
            var resource = _resourceDeserializer.Deserialize(response);
            return await RunValidationAsync(resource, profileUri);
        }

        [HttpGet]
        [Route(KnownRoutes.ValidateCodeValueset)]
        [AuditEventType(AuditEventSubType.ValidateCode)]
        public async Task<Parameters> ValidateCodeValueSet([FromRoute] string typeParameter, [FromRoute] string idParameter, [FromQuery] string system, [FromQuery] string code, [FromQuery] string display = null)
        {
            if (string.IsNullOrWhiteSpace(system) || string.IsNullOrWhiteSpace(code))
            {
                throw new BadRequestException("Must provide System and Code");
            }

            system = system.Trim(' ');
            code = code.Trim(' ');

            // Read resource from storage.
            Resource resource = null;
            try
            {
                RawResourceElement response = await _mediator.GetResourceAsync(new ResourceKey(typeParameter, idParameter), HttpContext.RequestAborted);
                resource = _resourceDeserializer.Deserialize(response).ToPoco();
            }
            catch (BadRequestException)
            {
                throw new BadRequestException("Unknown Valuset. Make sure ValueSet is in FHIR server");
            }

            return await RunValidateCodeValueSetAsync(resource, system, idParameter, code, display);
        }

        [HttpPost]
        [Route(KnownRoutes.ValidateResourceTypeById)]
        [AuditEventType(AuditEventSubType.Validate)]
        public async Task<IActionResult> ValidateByIdPost([FromBody] Resource resource, [FromRoute] string typeParameter, [FromRoute] string idParameter, [FromQuery] string profile)
        {
            ProcessResource(ref resource, ref profile);

            Uri profileUri = GetProfile(profile);
            ResourceElement resourceElement;
            if (resource == null)
            {
                // Read resource from storage.
                RawResourceElement serverResource = await _mediator.GetResourceAsync(new ResourceKey(typeParameter, idParameter), HttpContext.RequestAborted);

                // Convert it to fhir object.
                resourceElement = _resourceDeserializer.Deserialize(serverResource);
            }
            else
            {
                resourceElement = resource.ToResourceElement();
            }

            return await RunValidationAsync(resourceElement, profileUri);
        }

        private async Task<Parameters> RunValidateCodeValueSetAsync(Resource resource, string system, string idParameter, string code, string display)
        {
            ValidateCodeValueSetOperationResponse response = await _mediator.Send<ValidateCodeValueSetOperationResponse>(new ValidateCodeValueSetOperationRequest(resource, system, idParameter, code, display));
            return response.ParameterOutcome;
        }

        private async Task<IActionResult> RunValidationAsync(ResourceElement resource, Uri profile)
        {
            var response = await _mediator.Send<ValidateOperationResponse>(new ValidateOperationRequest(resource, profile));
            return FhirResult.Create(new OperationOutcome { Issue = response.Issues.Select(x => x.ToPoco()).ToList() }.ToResourceElement());
        }

        private static Uri GetProfile(string profile)
        {
            if (!string.IsNullOrEmpty(profile))
            {
                try
                {
                    return new Uri(profile);
                }
                catch
                {
                    throw new BadRequestException(string.Format(Resources.ProfileIsInvalid, profile));
                }
            }

            return null;
        }
    }
}
