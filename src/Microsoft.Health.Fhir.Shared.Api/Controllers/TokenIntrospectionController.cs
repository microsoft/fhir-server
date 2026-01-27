// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Api.Features.Exceptions;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    /// <summary>
    /// Controller implementing RFC 7662 token introspection endpoint.
    /// Supports introspection for both development (OpenIddict) and production (external IdP) tokens.
    /// </summary>
    [ServiceFilter(typeof(AuditLoggingFilterAttribute))]
    [TypeFilter(typeof(OAuth2ExceptionFilterAttribute))]
    [Authorize]
    [ValidateModelState]
    public class TokenIntrospectionController : Controller
    {
        private const string FormUrlEncodedContentType = "application/x-www-form-urlencoded";

        private readonly ITokenIntrospectionService _introspectionService;
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly ILogger<TokenIntrospectionController> _logger;

        public TokenIntrospectionController(
            ITokenIntrospectionService introspectionService,
            IAuthorizationService<DataActions> authorizationService,
            ILogger<TokenIntrospectionController> logger)
        {
            EnsureArg.IsNotNull(introspectionService, nameof(introspectionService));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _introspectionService = introspectionService;
            _authorizationService = authorizationService;
            _logger = logger;
        }

        /// <summary>
        /// Token introspection endpoint per RFC 7662.
        /// </summary>
        /// <param name="token">The token to introspect.</param>
        /// <returns>Token introspection response with active status and claims.</returns>
        [HttpPost]
        [Route("/connect/introspect")]
        [AuditEventType(AuditEventSubType.SmartOnFhirToken)]
        public async Task<IActionResult> Introspect([FromForm] string token)
        {
            // Verify the caller has any data action (if you can get a token, you can introspect a token)
            DataActions permittedActions = await _authorizationService.CheckAccess(DataActions.All, HttpContext.RequestAborted);
            if (permittedActions == DataActions.None)
            {
                _logger.LogWarning("Token introspection caller does not have any permitted data actions");
                return StatusCode((int)HttpStatusCode.Forbidden);
            }

            // Validate content-type per RFC 7662 Section 2.1
            // Must be application/x-www-form-urlencoded
            if (Request.ContentType == null ||
                !Request.ContentType.StartsWith(FormUrlEncodedContentType, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Token introspection request has invalid Content-Type: {ContentType}", Request.ContentType);
                throw new OAuth2BadRequestException("invalid_request", "Content-Type must be application/x-www-form-urlencoded");
            }

            // Validate token parameter is present per RFC 7662 Section 2.1
            if (string.IsNullOrWhiteSpace(token))
            {
                _logger.LogWarning("Token introspection request missing token parameter");
                throw new OAuth2BadRequestException("invalid_request", "token parameter is required");
            }

            // Delegate to introspection service
            var response = await _introspectionService.IntrospectTokenAsync(token, HttpContext.RequestAborted);
            return Ok(response);
        }
    }
}
