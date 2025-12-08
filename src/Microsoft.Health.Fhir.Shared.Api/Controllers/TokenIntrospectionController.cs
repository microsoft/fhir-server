// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Features.Security;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    /// <summary>
    /// Controller implementing RFC 7662 token introspection endpoint.
    /// Supports introspection for both development (OpenIddict) and production (external IdP) tokens.
    /// </summary>
    public class TokenIntrospectionController : Controller
    {
        private readonly ITokenIntrospectionService _introspectionService;
        private readonly ILogger<TokenIntrospectionController> _logger;

        public TokenIntrospectionController(
            ITokenIntrospectionService introspectionService,
            ILogger<TokenIntrospectionController> logger)
        {
            EnsureArg.IsNotNull(introspectionService, nameof(introspectionService));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _introspectionService = introspectionService;
            _logger = logger;
        }

        /// <summary>
        /// Token introspection endpoint per RFC 7662.
        /// </summary>
        /// <param name="token">The token to introspect.</param>
        /// <returns>Token introspection response with active status and claims.</returns>
        [HttpPost]
        [Route("/connect/introspect")]
        [Authorize]
        [AuditEventType(AuditEventSubType.SmartOnFhirToken)]
        public IActionResult Introspect([FromForm] string token)
        {
            // Validate token parameter is present
            if (string.IsNullOrWhiteSpace(token))
            {
                _logger.LogWarning("Token introspection request missing token parameter");
                return BadRequest(new { error = "invalid_request", error_description = "token parameter is required" });
            }

            // Delegate to introspection service
            var response = _introspectionService.IntrospectToken(token);
            return Ok(response);
        }
    }
}
