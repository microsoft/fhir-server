// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using EnsureThat;
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

        private async Task<Parameters> RunValidateCodeValueSetAsync(Resource resource, string system, string idParameter, string code, string display)
        {
            ValidateCodeValueSetOperationResponse response = await _mediator.Send<ValidateCodeValueSetOperationResponse>(new ValidateCodeValueSetOperationRequest(resource, system, idParameter, code, display));
            return response.ParameterOutcome;
        }
    }
}
