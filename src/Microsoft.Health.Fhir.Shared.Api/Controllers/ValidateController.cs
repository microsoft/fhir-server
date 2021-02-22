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
using Microsoft.Health.Fhir.Api.Features.ActionResults;
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
    [ServiceFilter(typeof(ValidateContentTypeFilterAttribute))]
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
        [AuditEventType(AuditEventSubType.Read)]
        public async Task<IActionResult> Validate([FromBody] Resource resource, [FromQuery(Name = "profile")] string profile, [FromQuery] bool? refresh)
        {
            Uri profileUri = null;
            if (resource.ResourceType == ResourceType.Parameters)
            {
                var parameterResource = (Parameters)resource;
                var profileFromParameters = parameterResource.Parameter.Find(param => param.Name.Equals("profile", StringComparison.OrdinalIgnoreCase));
                if (profileFromParameters != null)
                {
                    if (profile != null)
                    {
                        throw new BadRequestException(Api.Resources.MultipleProfilesProvided);
                    }

                    if (profileFromParameters.Value != null)
                    {
                        profile = profileFromParameters.Value.ToString();
                    }
                }

                var refreshFromParameters = parameterResource.Parameter.Find(param => param.Name.Equals("refresh", StringComparison.OrdinalIgnoreCase));
                if (refreshFromParameters != null)
                {
                    if (refresh != null)
                    {
                        throw new BadRequestException(Api.Resources.MultipleProfilesProvided);
                    }

                    if (refreshFromParameters.Value != null)
                    {
                        refresh = bool.Parse(refreshFromParameters.Value.ToString());
                    }
                }

                resource = parameterResource.Parameter.Find(param => param.Name.Equals("resource", StringComparison.OrdinalIgnoreCase)).Resource;
            }

            if (!string.IsNullOrEmpty(profile))
            {
                try
                {
                    profileUri = new Uri(profile);
                }
                catch
                {
                    throw new BadRequestException(string.Format(Resources.ProfileIsInvalid, profile));
                }
            }

            return await RunValidationAsync(resource.ToResourceElement(), refresh, profileUri);
        }

        [HttpGet]
        [Route(KnownRoutes.ValidateResourceTypeById)]
        [AuditEventType(AuditEventSubType.Read)]
        public async Task<IActionResult> ValidateById([FromRoute] string typeParameter, [FromRoute] string idParameter, [FromQuery] Uri profile, [FromQuery] bool? refresh)
        {
            // Read resource from storage.
            RawResourceElement response = await _mediator.GetResourceAsync(new ResourceKey(typeParameter, idParameter), HttpContext.RequestAborted);

            // Convert it to fhir object.
            var resource = _resourceDeserializer.Deserialize(response);
            return await RunValidationAsync(resource, refresh, profile);
        }

        private async Task<IActionResult> RunValidationAsync(ResourceElement resource, bool? refresh, Uri profile)
        {
            var response = await _mediator.Send<ValidateOperationResponse>(new ValidateOperationRequest(resource, profile, refresh ?? false));

            return FhirResult.Create(new OperationOutcome
            {
                Issue = response.Issues.Select(x => x.ToPoco()).ToList(),
            }.ToResourceElement());
        }
    }
}
