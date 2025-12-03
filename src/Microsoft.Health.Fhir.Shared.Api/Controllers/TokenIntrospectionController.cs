// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using EnsureThat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    /// <summary>
    /// Controller implementing RFC 7662 token introspection endpoint.
    /// Supports introspection for both development (OpenIddict) and production (external IdP) tokens.
    /// </summary>
    public class TokenIntrospectionController : Controller
    {
        private readonly SecurityConfiguration _securityConfiguration;
        private readonly ILogger<TokenIntrospectionController> _logger;
        private readonly JwtSecurityTokenHandler _tokenHandler;

        public TokenIntrospectionController(
            IOptions<SecurityConfiguration> securityConfiguration,
            ILogger<TokenIntrospectionController> logger)
        {
            EnsureArg.IsNotNull(securityConfiguration, nameof(securityConfiguration));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _securityConfiguration = securityConfiguration.Value;
            _logger = logger;
            _tokenHandler = new JwtSecurityTokenHandler();
        }

        /// <summary>
        /// Token introspection endpoint per RFC 7662.
        /// </summary>
        /// <param name="token">The token to introspect.</param>
        /// <returns>Token introspection response with active status and claims.</returns>
        [HttpPost]
        [Route("/connect/introspect")]
        [Authorize]
        [Consumes("application/x-www-form-urlencoded")]
        public IActionResult Introspect([FromForm] string token)
        {
            // Validate token parameter is present
            if (string.IsNullOrWhiteSpace(token))
            {
                _logger.LogWarning("Token introspection request missing token parameter");
                return BadRequest(new { error = "invalid_request", error_description = "token parameter is required" });
            }

            try
            {
                // Attempt to validate the token
                var validationResult = ValidateToken(token);

                if (validationResult.IsValid)
                {
                    // Build active response with claims
                    var response = BuildActiveResponse(validationResult.Token, validationResult.Principal);
                    _logger.LogInformation("Token introspection successful for token with sub: {Subject}", validationResult.Principal.FindFirst("sub")?.Value);
                    return Ok(response);
                }
                else
                {
                    // Return inactive response for invalid tokens
                    _logger.LogDebug("Token introspection returned inactive: {Reason}", validationResult.Reason);
                    return Ok(BuildInactiveResponse());
                }
            }
            catch (Exception ex)
            {
                // Never reveal why a token is invalid per RFC 7662 security guidance
                _logger.LogWarning(ex, "Token introspection failed with exception");
                return Ok(BuildInactiveResponse());
            }
        }

        /// <summary>
        /// Validates a JWT token using configured validation parameters.
        /// </summary>
        private TokenValidationResult ValidateToken(string token)
        {
            try
            {
                // First, try to parse the token to extract basic info
                JwtSecurityToken jwtToken;
                try
                {
                    jwtToken = _tokenHandler.ReadJwtToken(token);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to parse JWT token");
                    return TokenValidationResult.Invalid("malformed_token");
                }

                // Check if token is expired (quick check before full validation)
                if (jwtToken.ValidTo < DateTime.UtcNow)
                {
                    _logger.LogDebug("Token expired at {ExpirationTime}", jwtToken.ValidTo);
                    return TokenValidationResult.Invalid("expired");
                }

                // Build validation parameters
                var validationParameters = GetTokenValidationParameters();

                // Validate token signature and claims
                var principal = _tokenHandler.ValidateToken(token, validationParameters, out var validatedToken);

                return TokenValidationResult.Valid(jwtToken, principal);
            }
            catch (SecurityTokenExpiredException ex)
            {
                _logger.LogDebug(ex, "Token validation failed: expired");
                return TokenValidationResult.Invalid("expired");
            }
            catch (SecurityTokenException ex)
            {
                _logger.LogDebug(ex, "Token validation failed: security token exception");
                return TokenValidationResult.Invalid("invalid");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Token validation failed with unexpected exception");
                return TokenValidationResult.Invalid("error");
            }
        }

        /// <summary>
        /// Builds TokenValidationParameters from SecurityConfiguration.
        /// </summary>
        private TokenValidationParameters GetTokenValidationParameters()
        {
            var authority = _securityConfiguration.Authentication.Authority;
            var audience = _securityConfiguration.Authentication.Audience;

            // Configure OpenID Connect configuration retriever for JWKS
            var configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                $"{authority}/.well-known/openid-configuration",
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever());

            return new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = authority,
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKeyResolver = (token, securityToken, kid, validationParameters) =>
                {
                    // Retrieve signing keys from OpenID Connect configuration
                    var config = configurationManager.GetConfigurationAsync().GetAwaiter().GetResult();
                    return config.SigningKeys;
                },
                ClockSkew = TimeSpan.FromMinutes(5), // Allow 5 minutes clock skew
            };
        }

        /// <summary>
        /// Builds an RFC 7662 compliant active token response.
        /// </summary>
        private Dictionary<string, object> BuildActiveResponse(JwtSecurityToken token, ClaimsPrincipal principal)
        {
            var response = new Dictionary<string, object>
            {
                ["active"] = true,
                ["token_type"] = "Bearer",
            };

            // Extract standard claims
            AddClaimIfPresent(response, "sub", principal);
            AddClaimIfPresent(response, "iss", principal);
            AddClaimIfPresent(response, "aud", principal);

            // Add exp and iat as Unix timestamps
            if (token.ValidTo != DateTime.MinValue)
            {
                response["exp"] = new DateTimeOffset(token.ValidTo).ToUnixTimeSeconds();
            }

            if (token.ValidFrom != DateTime.MinValue)
            {
                response["iat"] = new DateTimeOffset(token.ValidFrom).ToUnixTimeSeconds();
            }

            // Extract client_id (use sub if client_id not present)
            var clientId = principal.FindFirst("client_id")?.Value ?? principal.FindFirst("sub")?.Value;
            if (!string.IsNullOrEmpty(clientId))
            {
                response["client_id"] = clientId;
            }

            // Extract username from name claim
            AddClaimIfPresent(response, "username", principal, "name");

            // Extract scope - check for raw_scope first (SMART v2), then scope, then scp
            var scope = principal.FindFirst("raw_scope")?.Value
                       ?? principal.FindFirst("scope")?.Value
                       ?? GetScopeFromMultipleClaims(principal);

            if (!string.IsNullOrEmpty(scope))
            {
                response["scope"] = scope;
            }

            // Add SMART-specific claims
            AddClaimIfPresent(response, "patient", principal);
            AddClaimIfPresent(response, "fhirUser", principal);

            return response;
        }

        /// <summary>
        /// Builds an RFC 7662 compliant inactive token response.
        /// </summary>
        private static Dictionary<string, object> BuildInactiveResponse()
        {
            return new Dictionary<string, object>
            {
                ["active"] = false,
            };
        }

        /// <summary>
        /// Adds a claim to the response if present in the principal.
        /// </summary>
        private static void AddClaimIfPresent(
            Dictionary<string, object> response,
            string key,
            ClaimsPrincipal principal,
            string claimType = null)
        {
            claimType ??= key;
            var claim = principal.FindFirst(claimType);
            if (claim != null && !string.IsNullOrEmpty(claim.Value))
            {
                response[key] = claim.Value;
            }
        }

        /// <summary>
        /// Gets scope from multiple scope claims (scp claim pattern).
        /// </summary>
        private string GetScopeFromMultipleClaims(ClaimsPrincipal principal)
        {
            // Check all configured scope claim names
            var scopeClaimNames = _securityConfiguration.Authorization.ScopesClaim ?? new List<string> { "scp" };

            foreach (var claimName in scopeClaimNames)
            {
                var scopeClaims = principal.FindAll(claimName).ToList();
                if (scopeClaims.Any())
                {
                    // Join multiple scope claims with space separator
                    return string.Join(" ", scopeClaims.Select(c => c.Value));
                }
            }

            return null;
        }

        /// <summary>
        /// Result of token validation.
        /// </summary>
        private class TokenValidationResult
        {
            public bool IsValid { get; private set; }

            public JwtSecurityToken Token { get; private set; }

            public ClaimsPrincipal Principal { get; private set; }

            public string Reason { get; private set; }

            public static TokenValidationResult Valid(JwtSecurityToken token, ClaimsPrincipal principal)
            {
                return new TokenValidationResult
                {
                    IsValid = true,
                    Token = token,
                    Principal = principal,
                };
            }

            public static TokenValidationResult Invalid(string reason)
            {
                return new TokenValidationResult
                {
                    IsValid = false,
                    Reason = reason,
                };
            }
        }
    }
}
