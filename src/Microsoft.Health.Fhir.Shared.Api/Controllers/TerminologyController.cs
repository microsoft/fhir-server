// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations;
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
        private readonly IOptions<OperationsConfiguration> _operationsConfig;

        public TerminologyController(IMediator mediator, ResourceDeserializer resourceDeserializer, IOptions<OperationsConfiguration> operationsConfig)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            _mediator = mediator;
            _resourceDeserializer = resourceDeserializer;
            _operationsConfig = operationsConfig;
        }

        [HttpGet]
        [Route(KnownRoutes.ValidateCodeGET)]
        [ServiceFilter(typeof(ValidateCodeParametersFilter))]
        [AuditEventType(AuditEventSubType.ValidateCode)]
        public async Task<Parameters> ValidateCodeGET([FromRoute] string typeParameter, [FromRoute] string idParameter, [FromQuery] string system, [FromQuery] string code, [FromQuery] string display = null)
        {
            CheckValidateCodeIsEnabled();

            // Read resource from database.
            RawResourceElement response = await _mediator.GetResourceAsync(new ResourceKey(typeParameter, idParameter), HttpContext.RequestAborted);
            Resource resource = _resourceDeserializer.Deserialize(response).ToPoco();

            return await RunValidateCodeGETAsync(resource, idParameter, code?.Trim(' '), system?.Trim(' '), display);
        }

        private async Task<Parameters> RunValidateCodeGETAsync(Resource resource, string idParameter, string code, string system, string display)
        {
            ValidateCodeOperationResponse response = await _mediator.Send<ValidateCodeOperationResponse>(new ValidateCodeOperationRequest(resource, idParameter, code, system, display));
            return response.ParameterOutcome;
        }

        [HttpPost]
        [Route(KnownRoutes.ValidateCodePOST)]
        [AuditEventType(AuditEventSubType.ValidateCode)]
        [ServiceFilter(typeof(ValidateCodeParametersFilter))]
        public async Task<Parameters> ValidateCodePOST([FromBody] Parameters parameters)
        {
            CheckValidateCodeIsEnabled();
            return await RunValidateCodePOSTAsync(parameters);
        }

        private async Task<Parameters> RunValidateCodePOSTAsync(Resource resource)
        {
            ValidateCodeOperationResponse response = await _mediator.Send<ValidateCodeOperationResponse>(new ValidateCodeOperationRequest(resource));
            return response.ParameterOutcome;
        }

        [HttpGet]
        [Route(KnownRoutes.LookUp)]
        [ServiceFilter(typeof(LookupParametersFilter))]
        [AuditEventType(AuditEventSubType.LookUp)]
        public async Task<Parameters> LookupCodeGET([FromQuery] string system, [FromQuery] string code)
        {
            CheckLookUpIsEnabled();
            return await RunLookUpCodeAsync(system.Trim(' '), code.Trim(' '));
        }

        [HttpPost]
        [Route(KnownRoutes.LookUp)]
        [ServiceFilter(typeof(LookupParametersFilter))]
        [AuditEventType(AuditEventSubType.LookUp)]
        public async Task<Parameters> LookupCodePOST([FromBody] Parameters parameters)
        {
            CheckLookUpIsEnabled();
            return await RunLookUpCodeAsync(string.Empty, string.Empty, parameters);
        }

        private async Task<Parameters> RunLookUpCodeAsync(string system, string code, Parameters parameter = null)
        {
            CheckLookUpIsEnabled();
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

        // TODO: Add DateTime date to supported parameters
        [HttpGet]
        [Route(KnownRoutes.ExpandWithId)]
        [ServiceFilter(typeof(ExpandParametersFilter))]
        [AuditEventType(AuditEventSubType.Expand)]
        public async Task<Resource> ExpandWithIdGET([FromRoute] string idParameter, [FromQuery] int offset = 0, [FromQuery] int count = 0)
        {
            CheckExpandIsEnabled();

            // Read resource from database.
            RawResourceElement response = await _mediator.GetResourceAsync(new ResourceKey(KnownResourceTypes.ValueSet, idParameter), HttpContext.RequestAborted);
            Resource resource = _resourceDeserializer.Deserialize(response).ToPoco();

            return await ExpandAsync(resource, offset: offset, count: count);
        }

        [HttpGet]
        [Route(KnownRoutes.ExpandWithoutId)]
        [ServiceFilter(typeof(ExpandParametersFilter))]
        [AuditEventType(AuditEventSubType.Expand)]
        public async Task<Resource> ExpandGET([FromQuery] string url, [FromQuery] int offset = 0, [FromQuery] int count = 0)
        {
            CheckExpandIsEnabled();
            return await ExpandAsync(url: url, offset: offset, count: count);
        }

        [HttpPost]
        [Route(KnownRoutes.ExpandWithoutId)]
        [ServiceFilter(typeof(ExpandParametersFilter))]
        [AuditEventType(AuditEventSubType.Expand)]
        public async Task<Resource> ExpandPOST([FromBody] Parameters parameters)
        {
            CheckExpandIsEnabled();
            ExpandOperationResponse response = await _mediator.Send<ExpandOperationResponse>(new ExpandOperationRequest(parameters));
            return response.ValueSetOutcome;
        }

        private async Task<Resource> ExpandAsync(Resource valueSet = null, string url = null, int offset = 0, int count = 0)
        {
            ExpandOperationResponse response = await _mediator.Send<ExpandOperationResponse>(new ExpandOperationRequest(valueSet, canonicalURL: url, offset: offset, count: count));
            return response.ValueSetOutcome;
        }

        private void CheckValidateCodeIsEnabled()
        {
            if (!_operationsConfig.Value.Terminology.ValidateCodeEnabled)
            {
                throw new RequestNotValidException(string.Format(Resources.OperationNotEnabled, OperationsConstants.ValidateCode));
            }
        }

        private void CheckLookUpIsEnabled()
        {
            if (!_operationsConfig.Value.Terminology.LookupEnabled)
            {
                throw new RequestNotValidException(string.Format(Resources.OperationNotEnabled, OperationsConstants.Lookup));
            }
        }

        private void CheckExpandIsEnabled()
        {
            if (!_operationsConfig.Value.Terminology.ExpandEnabled)
            {
                throw new RequestNotValidException(string.Format(Resources.OperationNotEnabled, OperationsConstants.Expand));
            }
        }
    }
}
