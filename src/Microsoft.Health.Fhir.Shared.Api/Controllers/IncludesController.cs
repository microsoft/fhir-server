// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Extensions;
using Microsoft.Health.Fhir.Api.Features.ActionResults;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    [ServiceFilter(typeof(OperationOutcomeExceptionFilterAttribute))]
    public class IncludesController : Controller
    {
        private readonly IMediator _mediator;
        private readonly CoreFeatureConfiguration _coreFeaturesConfig;

        public IncludesController(IMediator mediator, IOptions<CoreFeatureConfiguration> coreFeatures)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNull(coreFeatures?.Value, nameof(coreFeatures));

            _mediator = mediator;
            _coreFeaturesConfig = coreFeatures.Value;
        }

        [HttpGet]
        [Route(KnownRoutes.IncludesResourceType, Name = RouteNames.Includes)]
        [AuditEventType(AuditEventSubType.SearchSystem)]
        public async Task<IActionResult> Search(string typeParameter)
        {
            ResourceElement response = await _mediator.SearchIncludeResourceAsync(
                typeParameter,
                Request.GetQueriesForSearch(),
                HttpContext.RequestAborted);

            return FhirResult.Create(response);
        }
    }
}
