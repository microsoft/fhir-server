// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{
    public class DefaultAuthenticationAuthorityProvider : IAuthenticationAuthorityProvider
    {
        private const string AuthorizationEndpointPath = "authorize";
        private const string TokenEndpointPath = "token";

        private readonly SecurityConfiguration _securityConfiguration;

        public DefaultAuthenticationAuthorityProvider(
            IOptions<SecurityConfiguration> securityConfiguration)
        {
            EnsureArg.IsNotNull(securityConfiguration?.Value, nameof(securityConfiguration));

            _securityConfiguration = securityConfiguration.Value;
        }

        public string GetAuthorizationBaseEndpoint()
        {
            return _securityConfiguration.Authentication?.Authority;
        }

        public string GetAuthorizationEndpoint()
        {
            return $"{_securityConfiguration.Authentication?.Authority}/{AuthorizationEndpointPath}";
        }

        public string GetTokenEndpoint()
        {
            return $"{_securityConfiguration.Authentication?.Authority}/{TokenEndpointPath}";
        }
    }
}
