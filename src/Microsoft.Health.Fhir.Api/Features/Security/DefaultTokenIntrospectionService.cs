// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Microsoft.Health.Fhir.Api.Features.Security
{
    /// <summary>
    /// Default implementation of token introspection.
    /// </summary>
    public class DefaultTokenIntrospectionService : ITokenIntrospectionService
    {
        /// <summary>
        /// Named HttpClient for retrieving OIDC configuration documents.
        /// </summary>
        public const string OidcConfigurationHttpClientName = "OidcConfiguration";

        private readonly SecurityConfiguration _securityConfiguration;
        private readonly JwtSecurityTokenHandler _tokenHandler;
        private readonly ILogger<DefaultTokenIntrospectionService> _logger;
        private readonly ConfigurationManager<OpenIdConnectConfiguration> _configurationManager;

        public DefaultTokenIntrospectionService(
            IOptions<SecurityConfiguration> securityConfiguration,
            ILogger<DefaultTokenIntrospectionService> logger,
            IHttpClientFactory httpClientFactory)
        {
            EnsureArg.IsNotNull(securityConfiguration, nameof(securityConfiguration));
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(httpClientFactory, nameof(httpClientFactory));

            _securityConfiguration = securityConfiguration.Value;
            _tokenHandler = new JwtSecurityTokenHandler();
            _logger = logger;

            // Initialize configuration manager with named HttpClient
            // Named HttpClient is designed for long-lived use and is managed by IHttpClientFactory
            var authority = _securityConfiguration.Authentication.Authority.TrimEnd('/');
            var httpClient = httpClientFactory.CreateClient(OidcConfigurationHttpClientName);

            _configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                $"{authority}/.well-known/openid-configuration",
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever(httpClient));
        }

        /// <summary>
        /// Gets the security configuration for this service.
        /// </summary>
        protected SecurityConfiguration SecurityConfiguration => _securityConfiguration;

        /// <summary>
        /// Gets the JWT token handler for parsing and validating tokens.
        /// </summary>
        protected JwtSecurityTokenHandler TokenHandler => _tokenHandler;

        /// <inheritdoc />
        public async Task<Dictionary<string, object>> IntrospectTokenAsync(string token, CancellationToken cancellationToken = default)
        {
            try
            {
                // Attempt to validate the token
                var validationResult = await ValidateTokenAsync(token, cancellationToken);

                if (validationResult.IsValid)
                {
                    // Build active response with claims
                    var response = BuildActiveResponse(validationResult.Token, validationResult.Principal);
                    _logger.LogInformation("Token introspection successful for token with sub: {Subject}", validationResult.Principal.FindFirst("sub")?.Value);
                    return response;
                }
                else
                {
                    // Return inactive response for invalid tokens
                    _logger.LogInformation("Token introspection returned inactive: {Reason}", validationResult.Reason);
                    return BuildInactiveResponse();
                }
            }
            catch (Exception ex)
            {
                // Never reveal why a token is invalid per RFC 7662 security guidance
                _logger.LogWarning(ex, "Token introspection failed with exception");
                return BuildInactiveResponse();
            }
        }

        /// <summary>
        /// Validates a JWT token using configured validation parameters.
        /// </summary>
        protected virtual async Task<TokenValidationResult> ValidateTokenAsync(string token, CancellationToken cancellationToken = default)
        {
            try
            {
                // First, try to parse the token to extract basic info
                JwtSecurityToken jwtToken;
                try
                {
                    jwtToken = TokenHandler.ReadJwtToken(token);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to parse JWT token");
                    return TokenValidationResult.Invalid("malformed_token");
                }

                // Build validation parameters
                var validationParameters = await GetTokenValidationParametersAsync(cancellationToken);

                // Validate token signature and claims
                var principal = TokenHandler.ValidateToken(token, validationParameters, out var validatedToken);

                return TokenValidationResult.Valid(jwtToken, principal);
            }
            catch (SecurityTokenExpiredException ex)
            {
                _logger.LogInformation(ex, "Token validation failed: expired");
                return TokenValidationResult.Invalid("expired");
            }
            catch (SecurityTokenException ex)
            {
                _logger.LogInformation(ex, "Token validation failed: security token exception");
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
        protected virtual async Task<TokenValidationParameters> GetTokenValidationParametersAsync(CancellationToken cancellationToken = default)
        {
            var authority = SecurityConfiguration.Authentication.Authority;
            var audience = SecurityConfiguration.Authentication.Audience;

            // Normalize authority to ensure consistent JWKS endpoint
            var normalizedAuthority = authority.TrimEnd('/');

            // Pre-fetch the OpenID Connect configuration
            var config = await _configurationManager.GetConfigurationAsync(cancellationToken);

            return new TokenValidationParameters
            {
                ValidateIssuer = true,

                // Accept issuer with or without trailing slash (common OpenIddict variation)
                ValidIssuers = new[] { normalizedAuthority, normalizedAuthority + "/" },
                ValidateAudience = true,
                ValidAudience = audience,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = config.SigningKeys,
                ClockSkew = TimeSpan.FromMinutes(5), // Allow 5 minutes clock skew
            };
        }

        /// <summary>
        /// Builds an RFC 7662 compliant active token response.
        /// </summary>
        protected Dictionary<string, object> BuildActiveResponse(JwtSecurityToken token, ClaimsPrincipal principal)
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
        protected static Dictionary<string, object> BuildInactiveResponse()
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
        protected string GetScopeFromMultipleClaims(ClaimsPrincipal principal)
        {
            // Check all configured scope claim names
            var scopeClaimNames = SecurityConfiguration.Authorization.ScopesClaim ?? new List<string> { "scp" };

            // Find the first claim name that has associated claims
            var scopeClaims = scopeClaimNames
                .Select(claimName => principal.FindAll(claimName))
                .FirstOrDefault(claims => claims.Any());

            // Join multiple scope claims with space separator
            return scopeClaims != null
                ? string.Join(" ", scopeClaims.Select(c => c.Value))
                : null;
        }

        /// <summary>
        /// Result of token validation.
        /// </summary>
        protected class TokenValidationResult
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
