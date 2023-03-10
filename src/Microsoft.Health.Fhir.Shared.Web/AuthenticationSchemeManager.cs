// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

// --------------------------------------------------------------------------
// <copyright file="AuthenticationSchemeManager.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation. All rights reserved.
// </copyright>
// --------------------------------------------------------------------------

/*using System.Collections.Concurrent;*/
using System.Collections.Generic;
/*using System.IdentityModel.Tokens.Jwt;*/
using System.Linq;
/*using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Options;
/*using Microsoft.Health.Cloud.AccountRoutingService.Dtos;
using Microsoft.Health.Cloud.ServicePlatform.Http;
using Microsoft.Health.Fhir.Cloud.FhirService.Features.Settings;
using Microsoft.Health.Fhir.Core.Configs;*/
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.S2S;
using Microsoft.IdentityModel.S2S.Configuration;

namespace Microsoft.Health.Fhir.Cloud.FhirService.Features.Security
{
    public class AuthenticationSchemeManager : /*IFhirServerSettingsUpdateHandler,*/ IInboundPolicyProvider, IS2SAuthenticationManagerPostConfigure
    {
        /*private readonly ITokenIssuerProvider _issuerProvider;
        private readonly JwtSecurityTokenHandler _tokenHandler = new JwtSecurityTokenHandler();
        private readonly string _servicePrincipalClientId;

        // Reference to this object is atomically read/modified by multiple threads. Set it to be volatile to prevent optimizer from doing "funny stuff".
        private volatile Dictionary<string, AuthenticationParameters> _authenticationParametersMap = new Dictionary<string, AuthenticationParameters>(); // WARNING, multiple threads accessing REFERENCE to this object.*/

        /*public AuthenticationSchemeManager(
            IOptions<SecurityConfiguration> securityConfiguration,
            ITokenIssuerProvider issuerProvider)*/
        public AuthenticationSchemeManager()
        {
            /*EnsureArg.IsNotNull(securityConfiguration?.Value?.Authentication, nameof(securityConfiguration.Value.Authentication));
            EnsureArg.IsOfType(securityConfiguration.Value.Authentication, typeof(FhirServiceAuthenticationConfiguration), nameof(securityConfiguration.Value.Authentication));
            EnsureArg.IsNotNull(securityConfiguration?.Value?.Authentication.Authority, nameof(securityConfiguration.Value.Authentication.Authority));
            EnsureArg.IsNotNull(securityConfiguration?.Value?.Authentication.Audience, nameof(securityConfiguration.Value.Authentication.Audience));
            _servicePrincipalClientId = EnsureArg.IsNotNull(securityConfiguration?.Value?.ServicePrincipalClientId, nameof(securityConfiguration.Value.ServicePrincipalClientId));
            _issuerProvider = EnsureArg.IsNotNull(issuerProvider, nameof(issuerProvider));*/
        }

        public IEnumerable<S2SInboundPolicy> GetPolicies(IEnumerable<S2SInboundPolicy> configuredPolicies, HttpRequestData httpRequestData, S2SContext context)
        {
            /*// Ensure the request has a valid bearer token and extract the token from the header.
            if (httpRequestData.Headers.TryGetTokenStringFromAuthorizationHeader("bearer", out string token))
            {
                try
                {
                    JwtSecurityToken jwtToken = _tokenHandler.ReadJwtToken(token);
                    if (_authenticationParametersMap.TryGetValue(jwtToken.Issuer, out AuthenticationParameters authenticationParameters))
                    {
                        var policy = new S2SInboundPolicy(authenticationParameters.Authority, _servicePrincipalClientId) { ApplyPolicyForAllTenants = true };
                        policy.ValidAudiences.Add(authenticationParameters.Audience);
                        return new S2SInboundPolicy[] { policy };
                    }
                }
                catch
                {
                    return configuredPolicies;
                }
            }*/

            return configuredPolicies;
        }

        /*public async Task HandleUpdate(FhirServerSettings settings, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(settings, nameof(settings));
            IDictionary<string, FhirServerAuthSetting> authSettingsMap = await _issuerProvider.CreateIssuerMap(settings.AuthSettings).ConfigureAwait(false);
            SetAuthSchemes(authSettingsMap);
        }

        private void SetAuthSchemes(IDictionary<string, FhirServerAuthSetting> authSettingsMap)
        {
            EnsureArg.IsNotNull(authSettingsMap, nameof(authSettingsMap));

            // In this thread, create new map.
            var newAuthenticationParametersMap = new Dictionary<string, AuthenticationParameters>();
            foreach (KeyValuePair<string, FhirServerAuthSetting> entry in authSettingsMap)
            {
                newAuthenticationParametersMap.TryAdd(entry.Key, new AuthenticationParameters(entry.Value.Authority, entry.Value.Audience));
            }

            // Now atomically set the reference to the new map. Other threads may still hold a reference to, and use the old map.
            _authenticationParametersMap = newAuthenticationParametersMap;
        }*/

        public S2SAuthenticationManager PostConfigure(S2SAuthenticationManager s2sAuthenticationManager)
        {
            var jwtHandler = s2sAuthenticationManager.AuthenticationHandlers.First() as JwtAuthenticationHandler;
            jwtHandler.InboundPolicyProvider = this;
            return s2sAuthenticationManager;
        }

        /*private class AuthenticationParameters
        {
            public AuthenticationParameters(string authority, string audience)
            {
                Authority = authority;
                Audience = audience;
            }

            public string Authority { get; }

            public string Audience { get; }
        }*/
    }
}
