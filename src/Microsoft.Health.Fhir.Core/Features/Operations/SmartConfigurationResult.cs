// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations
{
    /// <summary>
    /// Class used to hold data that needs to be returned to the client when the smart configuration operation completes.
    /// </summary>
    public class SmartConfigurationResult
    {
        public SmartConfigurationResult(Uri authorizationEndpoint, Uri tokenEndpoint, ICollection<string> capabilities)
        {
            EnsureArg.IsNotNull(authorizationEndpoint, nameof(authorizationEndpoint));
            EnsureArg.IsNotNull(tokenEndpoint, nameof(tokenEndpoint));
            EnsureArg.IsNotNull(capabilities, nameof(capabilities));

            AuthorizationEndpoint = authorizationEndpoint;
            TokenEndpoint = tokenEndpoint;
            Capabilities = capabilities;
        }

        public SmartConfigurationResult(
            Uri authorizationEndpoint,
            Uri tokenEndpoint,
            ICollection<string> capabilities,
            ICollection<string> scopesSupported,
            ICollection<string> codeChallengeMethodsSupported = null,
            ICollection<string> grantTypesSupported = null,
            ICollection<string> tokenEndpointAuthMethodsSupported = null,
            ICollection<string> responseTypesSupported = null,
            string introspectionEndpoint = null,
            string managementEndpoint = null,
            string revocationEndpoint = null)
        {
            EnsureArg.IsNotNull(authorizationEndpoint, nameof(authorizationEndpoint));
            EnsureArg.IsNotNull(tokenEndpoint, nameof(tokenEndpoint));
            EnsureArg.IsNotNull(capabilities, nameof(capabilities));

            AuthorizationEndpoint = authorizationEndpoint;
            TokenEndpoint = tokenEndpoint;
            Capabilities = capabilities;
            ScopesSupported = scopesSupported;
            CodeChallengeMethodsSupported = codeChallengeMethodsSupported;
            GrantTypesSupported = grantTypesSupported;
            TokenEndpointAuthMethodsSupported = tokenEndpointAuthMethodsSupported;
            ResponseTypesSupported = responseTypesSupported;
            IntrospectionEndpoint = introspectionEndpoint;
            ManagementEndpoint = managementEndpoint;
            RevocationEndpoint = revocationEndpoint;
        }

        [JsonConstructor]
        public SmartConfigurationResult()
        {
        }

        [JsonProperty("authorization_endpoint")]
        public Uri AuthorizationEndpoint { get; private set; }

        [JsonProperty("token_endpoint")]
        public Uri TokenEndpoint { get; private set; }

        [JsonProperty("capabilities")]
        public ICollection<string> Capabilities { get; private set; }

        [JsonProperty("scopes_supported")]
        public ICollection<string> ScopesSupported { get; private set; }

        [JsonProperty("code_challenge_methods_supported")]
        public ICollection<string> CodeChallengeMethodsSupported { get; }

        [JsonProperty("grant_types_supported")]
        public ICollection<string> GrantTypesSupported { get; }

        [JsonProperty("token_endpoint_auth_methods_supported")]
        public ICollection<string> TokenEndpointAuthMethodsSupported { get; }

        [JsonProperty("response_types_supported")]
        public ICollection<string> ResponseTypesSupported { get; }

        [JsonProperty("introspection_endpoint")]
        public string IntrospectionEndpoint { get; }

        [JsonProperty("management_endpoint")]
        public string ManagementEndpoint { get; }

        [JsonProperty("revocation_endpoint")]
        public string RevocationEndpoint { get; }
    }
}
