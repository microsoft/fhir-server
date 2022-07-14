// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Operation;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    [ServiceFilter(typeof(AuditLoggingFilterAttribute))]
    [ServiceFilter(typeof(OperationOutcomeExceptionFilterAttribute))]
    [ServiceFilter(typeof(ValidateFormatParametersAttribute))]
    [ValidateResourceTypeFilter(true)]

    [ValidateModelState]
    public class TerminologyController : Controller
    {
        private readonly IMediator _mediator;
        private readonly ResourceDeserializer _resourceDeserializer;

        public TerminologyController(IMediator mediator, ResourceDeserializer resourceDeserializer)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            _mediator = mediator;
            _resourceDeserializer = resourceDeserializer;
        }

        [HttpGet]
        [Route(KnownRoutes.ValidateCodeGET)]
        [AuditEventType(AuditEventSubType.ValidateCode)]
        public async Task<Parameters> ValidateCodeGET([FromRoute] string typeParameter, [FromRoute] string idParameter, [FromQuery] string system, [FromQuery] string code, [FromQuery] string display = null)
        {
            Resource resource = null;

            // Read resource from database.
            try
            {
                RawResourceElement response = await _mediator.GetResourceAsync(new ResourceKey(typeParameter, idParameter), HttpContext.RequestAborted);
                resource = _resourceDeserializer.Deserialize(response).ToPoco();
            }
            catch (BadRequestException)
            {
                throw new BadRequestException("Unknown Valuset or CodeSystem. Make sure resource is in FHIR server");
            }

            if (!string.IsNullOrWhiteSpace(code) && (!string.IsNullOrWhiteSpace(system) || typeParameter == "CodeSystem"))
            {
                return await RunValidateCodeGETAsync(resource, idParameter, code?.Trim(' '), system?.Trim(' '), display);
            }
            else
            {
                if (typeParameter == "CodeSystem")
                {
                    throw new BadRequestException("Must provide Code.");
                }
                else
                {
                    throw new BadRequestException("Must provide System and Code when validating code using valueset");
                }
            }
        }

        private async Task<Parameters> RunValidateCodeGETAsync(Resource resource, string idParameter, string code, string system, string display)
        {
            ValidateCodeOperationResponse response = await _mediator.Send<ValidateCodeOperationResponse>(new ValidateCodeOperationRequest(resource, idParameter, code, system, display));
            return response.ParameterOutcome;
        }

        [HttpPost]
        [Route(KnownRoutes.ValidateCodePOST)]
        [AuditEventType(AuditEventSubType.ValidateCode)]
        public async Task<Parameters> ValidateCodePOST([FromBody] Parameters parameters)
        {
            if (parameters.Parameter.Count != 2)
            {
                throw new BadRequestException("Please input proper parameters");
            }

            try
            {
                return await RunValidateCodePOSTAsync(parameters);
            }
            catch (Exception ex)
            {
                throw new BadRequestException(ex.Message);
            }
        }

        private async Task<Parameters> RunValidateCodePOSTAsync(Resource resource)
        {
            ValidateCodeOperationResponse response = await _mediator.Send<ValidateCodeOperationResponse>(new ValidateCodeOperationRequest(resource));
            return response.ParameterOutcome;
        }
    }
}
