// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Health.Fhir.Core.Features.Health;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    [TypeFilter(typeof(UnsupportedContentTypeFilter))]
    [Route("health")]
    public class HealthController : Controller
    {
        private readonly IHealthCheck _healthCheck;

        public HealthController(
            IHealthCheck healthCheck)
        {
            EnsureArg.IsNotNull(healthCheck, nameof(healthCheck));

            _healthCheck = healthCheck;
        }

        [HttpGet]
        [Route("check")]
        public async Task<IActionResult> Check()
        {
            HealthCheckResult result = await _healthCheck.CheckAsync(HttpContext.RequestAborted);

            if (result.HealthState == HealthState.Healthy)
            {
                return Ok(result);
            }

            return StatusCode((int)HttpStatusCode.ServiceUnavailable, result);
        }
    }
}
