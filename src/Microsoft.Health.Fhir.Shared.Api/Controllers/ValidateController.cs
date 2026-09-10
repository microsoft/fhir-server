// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using Medino;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Routing;
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
    public class ValidateController : Controller
    {
        private readonly IMediator _mediator;
        private readonly ResourceDeserializer _resourceDeserializer;
        private readonly ILogger<ValidateController> _logger;

        public ValidateController(IMediator mediator, ResourceDeserializer resourceDeserializer, ILogger<ValidateController> logger)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _mediator = mediator;
            _resourceDeserializer = resourceDeserializer;
            _logger = logger;
        }

        [HttpPost]
        [Route(KnownRoutes.ValidateResourceType)]
        [AuditEventType(AuditEventSubType.Validate)]
        public async Task<IActionResult> Validate([FromBody] Resource resource, [FromQuery(Name = "profile")] string profile)
        {
            ProcessResource(ref resource, ref profile);

            // This endpoint has no id to fall back to, so if the Parameters payload did not
            // carry an actual resource, there is nothing to validate.
            if (resource == null)
            {
                throw new BadRequestException(Resources.ValidateResourceRequired);
            }

            Uri profileUri = GetProfile(profile);

            return await RunValidationAsync(resource.ToResourceElement(), profileUri);
        }

        private static void ProcessResource(ref Resource resource, ref string profile)
        {
            if (resource?.TypeName == KnownResourceTypes.Parameters)
            {
                var parameterResource = (Parameters)resource;
                var profileFromParameters = parameterResource.Parameter?.Find(param => param.Name.Equals("profile", StringComparison.OrdinalIgnoreCase));
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

                var resourceParam = parameterResource.Parameter?.Find(param => param.Name.Equals("resource", StringComparison.OrdinalIgnoreCase));
                if (resourceParam != null)
                {
                    // Extract the inner resource even when it is null so callers with an id
                    // (e.g. ValidateByIdPost) can fall back to reading the resource from storage.
                    resource = resourceParam.Resource;
                }
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

        private async Task<IActionResult> RunValidationAsync(ResourceElement resource, Uri profile)
        {
            var response = await _mediator.SendAsync<ValidateOperationResponse>(new ValidateOperationRequest(resource, profile));
            return FhirResult.Create(_logger, new OperationOutcome { Issue = response.Issues.Select(x => x.ToPoco()).ToList() }.ToResourceElement());
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
