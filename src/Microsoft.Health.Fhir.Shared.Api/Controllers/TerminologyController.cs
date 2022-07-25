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

            if (!(string.Equals(typeParameter, "CodeSystem", StringComparison.OrdinalIgnoreCase) || string.Equals(typeParameter, "ValueSet", StringComparison.OrdinalIgnoreCase)))
            {
                throw new BadRequestException("$validate-code can only be called on a CodeSystem or Valueset");
            }

            // Read resource from database.
            RawResourceElement response = await _mediator.GetResourceAsync(new ResourceKey(typeParameter, idParameter), HttpContext.RequestAborted);
            resource = _resourceDeserializer.Deserialize(response).ToPoco();

            if (!string.IsNullOrEmpty(code) && !string.IsNullOrEmpty(system))
            {
                return await RunValidateCodeGETAsync(resource, idParameter, code?.Trim(' '), system?.Trim(' '), display);
            }
            else
            {
                throw new BadRequestException("Must provide System and Code");
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
                throw new BadRequestException("Parameter must provide a coding and ValueSet/CodeSystem parameter components");
            }

            if (!string.Equals(parameters.Parameter[0].Name, "coding", StringComparison.OrdinalIgnoreCase))
            {
                throw new BadRequestException("Must provide coding parameter componenet");
            }

            if (!(string.Equals(parameters.Parameter[1].Resource.TypeName, "CodeSystem", StringComparison.OrdinalIgnoreCase) || string.Equals(parameters.Parameter[1].Resource.TypeName, "ValueSet", StringComparison.OrdinalIgnoreCase)))
            {
                throw new BadRequestException("$validate-code can only be called on a CodeSystem or Valueset");
            }

            return await RunValidateCodePOSTAsync(parameters);
        }

        private async Task<Parameters> RunValidateCodePOSTAsync(Resource resource)
        {
            ValidateCodeOperationResponse response = await _mediator.Send<ValidateCodeOperationResponse>(new ValidateCodeOperationRequest(resource));
            return response.ParameterOutcome;
        }

        [HttpGet]
        [Route(KnownRoutes.LookUp)]
        [AuditEventType(AuditEventSubType.LookUp)]
        public async Task<Parameters> LookupCodeGET([FromRoute] string typeParameter, [FromQuery] string system, [FromQuery] string code)
        {
            if (!string.Equals(typeParameter, "CodeSystem", StringComparison.OrdinalIgnoreCase))
            {
                throw new BadRequestException("Resource type must be CodeSystem");
            }

            if (string.IsNullOrWhiteSpace(system) || string.IsNullOrWhiteSpace(code))
            {
                throw new BadRequestException("Must provide System and Code");
            }

            return await RunLookUpCodeAsync(system.Trim(' '), code.Trim(' '));
        }

        [HttpPost]
        [Route(KnownRoutes.LookUp)]
        [AuditEventType(AuditEventSubType.LookUp)]
        public async Task<Parameters> LookupCodePOST([FromBody] Resource resource)
        {
            if (!string.Equals(((Parameters)resource).Parameter[0].Name, "coding", StringComparison.OrdinalIgnoreCase))
            {
                throw new BadRequestException("Must provide coding parameter componenet");
            }

            return await RunLookUpCodeAsync(string.Empty, string.Empty, (Parameters)resource);
        }

        private async Task<Parameters> RunLookUpCodeAsync(string system, string code, Parameters parameter = null)
        {
            LookUpOperationResponse response = null;
            if (parameter != null)
            {
                response = await _mediator.Send<LookUpOperationResponse>(new LookUpOperationRequest(parameter));
            }
            else
            {
                response = await _mediator.Send<LookUpOperationResponse>(new LookUpOperationRequest(system, code));
            }

            return response.ParameterOutcome;
        }

        // TODO: add DateTIme date to supported parameters
        [HttpGet]
        [Route(KnownRoutes.ExpandWithId)]
        [AuditEventType(AuditEventSubType.Expand)]
        public async Task<Resource> ExpandWithIdGET(
            [FromRoute] string typeParameter,
            [FromRoute] string idParameter,
            [FromQuery] int offset = 0,
            [FromQuery] int count = 0)
        {
            Resource resource = null;
            if (typeParameter != "ValueSet")
            {
                throw new BadRequestException("$expand operation is only done on valuesets");
            }

            if (string.IsNullOrEmpty(idParameter))
            {
                throw new BadRequestException("Must provide Valueset ID");
            }

            // Read resource from database.
            RawResourceElement response = await _mediator.GetResourceAsync(new ResourceKey(typeParameter, idParameter), HttpContext.RequestAborted);
            resource = _resourceDeserializer.Deserialize(response).ToPoco();

            return await ExpandAsync(resource, offset: offset, count: count);
        }

        [HttpGet]
        [Route(KnownRoutes.ExpandGET)]
        [AuditEventType(AuditEventSubType.Expand)]
        public async Task<Resource> ExpandGET([FromRoute] string typeParameter, [FromQuery] string url = null, [FromQuery] int offset = 0, [FromQuery] int count = 0)
        {
            if (typeParameter != "ValueSet")
            {
                throw new BadRequestException("$expand operation is only done on valuesets");
            }

            if (string.IsNullOrEmpty(url))
            {
                throw new BadRequestException("Request must provide valueset canonicalURL");
            }

            return await ExpandAsync(url: url, offset: offset, count: count);
        }

        [HttpPost]
        [Route(KnownRoutes.ExpandGET)]
        [AuditEventType(AuditEventSubType.Expand)]
        public async Task<Resource> ExpandPOST([FromBody] Resource resource)
        {
            if (!string.Equals(((Parameters)resource).Parameter[0].Name, "valueSet", StringComparison.OrdinalIgnoreCase))
            {
                throw new BadRequestException("Must provide valueSet parameter componenet");
            }

            ExpandOperationResponse response = await _mediator.Send<ExpandOperationResponse>(new ExpandOperationRequest((Parameters)resource));
            return response.ValueSetOutcome;
        }

        private async Task<Resource> ExpandAsync(Resource valueSet = null, string url = null, int offset = 0, int count = 0)
        {
            ExpandOperationResponse response = await _mediator.Send<ExpandOperationResponse>(new ExpandOperationRequest(valueSet, canonicalURL: url, offset: offset, count: count));
            return response.ValueSetOutcome;
        }
    }
}
