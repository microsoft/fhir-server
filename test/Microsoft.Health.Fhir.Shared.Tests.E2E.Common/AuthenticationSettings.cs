// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.Health.Fhir.Api.OpenIddict.Configuration;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.IdentityModel.Tokens;
using static Microsoft.Health.Fhir.Tests.Common.EnvironmentVariables;

namespace Microsoft.Health.Fhir.Tests.E2E.Common
{
    /// <summary>
    /// Authentication Settings
    /// </summary>
    public static class AuthenticationSettings
    {
        public static string Scope => GetEnvironmentVariable(KnownEnvironmentVariableNames.AuthorizationScope, DevelopmentIdentityProviderConfiguration.Audience);

        public static string Resource => GetEnvironmentVariable(KnownEnvironmentVariableNames.AuthorizationResource, DevelopmentIdentityProviderConfiguration.Audience);

        /// <summary>
        /// Gets the token endpoint used by remote E2E client-credential authentication.
        /// </summary>
        public static Uri TestTokenEndpoint => ParseTestTokenEndpoint(GetEnvironmentVariable(KnownEnvironmentVariableNames.TestTokenEndpoint));

        public static bool IsThirdPartySmartTokenConfigured =>
            !string.IsNullOrWhiteSpace(GetEnvironmentVariable(KnownEnvironmentVariableNames.TestSmartTokenIssuer, null)) &&
            !string.IsNullOrWhiteSpace(GetEnvironmentVariable(KnownEnvironmentVariableNames.TestSmartTokenPrivateKey, null));

        public static string CreateThirdPartySmartToken(string clientId, string scope)
        {
            if (!IsThirdPartySmartTokenConfigured)
            {
                throw new InvalidOperationException("Third-party SMART token settings must be configured for remote SMART tests.");
            }

            using RSA rsa = RSA.Create();
            rsa.ImportFromPem(GetEnvironmentVariable(KnownEnvironmentVariableNames.TestSmartTokenPrivateKey, null));

            var signingKey = new RsaSecurityKey(rsa)
            {
                KeyId = GetKeyId(rsa),
            };
            var signingCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256);
            var claims = new[]
            {
                new System.Security.Claims.Claim("appid", clientId),
                new System.Security.Claims.Claim("scp", scope),
                new System.Security.Claims.Claim("scope", scope),
                new System.Security.Claims.Claim("roles", "smartUser"),
            };

            var additionalClaims = new System.Collections.Generic.List<System.Security.Claims.Claim>(claims);
            if (scope.Contains("patient/", StringComparison.Ordinal))
            {
                additionalClaims.Add(new System.Security.Claims.Claim("fhirUser", "Patient/1234567890"));
            }
            else if (scope.Contains("user/", StringComparison.Ordinal))
            {
                additionalClaims.Add(new System.Security.Claims.Claim("fhirUser", "Practitioner/1234567890"));
            }

            var token = new JwtSecurityToken(
                issuer: GetEnvironmentVariable(KnownEnvironmentVariableNames.TestSmartTokenIssuer, null),
                audience: Resource,
                claims: additionalClaims,
                notBefore: DateTime.UtcNow,
                expires: DateTime.UtcNow.AddHours(1),
                signingCredentials: signingCredentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        private static string GetKeyId(RSA rsa)
        {
            RSAParameters parameters = rsa.ExportParameters(false);
            string exponent = Base64UrlEncoder.Encode(parameters.Exponent);
            string modulus = Base64UrlEncoder.Encode(parameters.Modulus);
            string jwkThumbprint = $"{{\"e\":\"{exponent}\",\"kty\":\"RSA\",\"n\":\"{modulus}\"}}";
            return Base64UrlEncoder.Encode(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(jwkThumbprint)));
        }

        internal static Uri ParseTestTokenEndpoint(string tokenEndpoint)
        {
            if (string.IsNullOrWhiteSpace(tokenEndpoint))
            {
                throw new InvalidOperationException(
                    $"{KnownEnvironmentVariableNames.TestTokenEndpoint} must be configured for remote E2E tests.");
            }

            if (!Uri.TryCreate(tokenEndpoint, UriKind.Absolute, out Uri endpoint) ||
                (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException(
                    $"{KnownEnvironmentVariableNames.TestTokenEndpoint} must be an absolute HTTP or HTTPS URI. " +
                    $"Received '{tokenEndpoint}'.");
            }

            return endpoint;
        }
    }
}
