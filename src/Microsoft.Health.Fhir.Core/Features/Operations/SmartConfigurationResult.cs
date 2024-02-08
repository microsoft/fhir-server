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
    /// This class represents to /.well-known/smart-configuration endpoint response defined here: https://build.fhir.org/ig/HL7/smart-app-launch/conformance.html#metadata.
    /// Some of the properties are marked as CONDITIONAL, RECOMMENDED, OPTIONAL, and REQUIRED as per the specification but no validation is done here.
    /// </summary>
    public class SmartConfigurationResult
    {
        public SmartConfigurationResult(
            Uri issuer,
            Uri jwksUri,
            Uri authorizationEndpoint,
            ICollection<string> grantTypeSupported,
            Uri tokenEndpoint,
            ICollection<string> tokenEndpointAuthMethodsSupported,
            Uri registrationEndpoint,
            Uri smartAppStateEndpoint,
            Uri patientAccessBrandBundle,
            string patientAccessBrandIdentifier,
            ICollection<string> scopesSupported,
            ICollection<string> responseTypesSupported,
            Uri managementEndpoint,
            Uri introspectionEndpoint,
            Uri revocationEndpoint,
            ICollection<string> capabilities,
            ICollection<string> codeChallengeMethodsSupported)
        {
            Issuer = issuer;
            JwksUri = jwksUri;
            AuthorizationEndpoint = authorizationEndpoint;
            GrantTypeSupported = grantTypeSupported;
            TokenEndpoint = tokenEndpoint;
            TokenEndpointAuthMethodsSupported = tokenEndpointAuthMethodsSupported;
            RegistrationEndpoint = registrationEndpoint;
            SmartAppStateEndpoint = smartAppStateEndpoint;
            PatientAccessBrandBundle = patientAccessBrandBundle;
            PatientAccessBrandIdentifier = patientAccessBrandIdentifier;
            ScopesSupported = scopesSupported;
            ResponseTypesSupported = responseTypesSupported;
            ManagementEndpoint = managementEndpoint;
            IntrospectionEndpoint = introspectionEndpoint;
            RevocationEndpoint = revocationEndpoint;
            Capabilities = capabilities;
            CodeChallengeMethodsSupported = codeChallengeMethodsSupported;
        }

        [JsonConstructor]
        public SmartConfigurationResult()
        {
        }

        /// <summary>
        /// CONDITIONAL, String conveying this system’s OpenID Connect Issuer URL. Required if the server’s capabilities include sso-openid-connect; otherwise, omitted.
        /// </summary>
        [JsonProperty("issuer", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public Uri Issuer { get; }

        /// <summary>
        /// CONDITIONAL, String conveying this system’s JSON Web Key Set URL. Required if the server’s capabilities include sso-openid-connect; otherwise, optional.
        /// </summary>
        [JsonProperty("jwks_uri", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public Uri JwksUri { get; }

        /// <summary>
        /// REQUIRED, URL to the OAuth2 authorization endpoint.
        /// </summary>
        [JsonProperty("authorization_endpoint", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public Uri AuthorizationEndpoint { get; }

        /// <summary>
        /// REQUIRED, Array of grant types supported at the token endpoint. The options are “authorization_code” (when SMART App Launch is supported) and “client_credentials” (when SMART Backend Services is supported).
        /// </summary>
        [JsonProperty("grant_types_supported", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public ICollection<string> GrantTypeSupported { get; }

        /// <summary>
        /// REQUIRED, URL to the OAuth2 token endpoint.
        /// </summary>
        [JsonProperty("token_endpoint", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public Uri TokenEndpoint { get; }

        /// <summary>
        /// OPTIONAL, array of client authentication methods supported by the token endpoint. The options are “client_secret_post”, “client_secret_basic”, and “private_key_jwt”.
        /// </summary>
        [JsonProperty("token_endpoint_auth_methods_supported", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public ICollection<string> TokenEndpointAuthMethodsSupported { get; }

        /// <summary>
        /// OPTIONAL, If available, URL to the OAuth2 dynamic registration endpoint for this FHIR server.
        /// </summary>
        [JsonProperty("registration_endpoint", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public Uri RegistrationEndpoint { get; }

        /// <summary>
        /// CONDITIONAL, URL to the EHR’s app state endpoint. SHALL be present when the EHR supports the smart-app-state capability and the endpoint is distinct from the EHR’s primary endpoint.
        /// </summary>
        [JsonProperty("smart_app_state_endpoint", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public Uri SmartAppStateEndpoint { get; }

        /// <summary>
        /// RECOMMENDED, URL for a Brand Bundle. See https://build.fhir.org/ig/HL7/smart-app-launch/brands.html.
        /// </summary>
        [JsonProperty("patientAccessBrandBundle", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public Uri PatientAccessBrandBundle { get; }

        /// <summary>
        /// RECOMMENDED, Identifier for the primary entry in a Brand Bundle. See https://build.fhir.org/ig/HL7/smart-app-launch/brands.html..
        /// </summary>
        [JsonProperty("patientAccessBrandIdentifier", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string PatientAccessBrandIdentifier { get; }

        /// <summary>
        /// RECOMMENDED, Array of scopes a client may request. See https://build.fhir.org/ig/HL7/smart-app-launch/scopes-and-launch-context.html#quick-start. The server SHALL support all scopes listed here; additional scopes MAY be supported (so clients should not consider this an exhaustive list).
        /// </summary>
        [JsonProperty("scopes_supported", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public ICollection<string> ScopesSupported { get; }

        /// <summary>
        /// RECOMMENDED, Array of OAuth2 response_type values that are supported. Implementers can refer to response_types defined in OAuth 2.0 (RFC 6749 https://datatracker.ietf.org/doc/html/rfc6749) and in OIDC Core https://openid.net/specs/openid-connect-core-1_0.html#Authentication.
        /// </summary>
        [JsonProperty("response_types_supported", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public ICollection<string> ResponseTypesSupported { get; }

        /// <summary>
        /// RECOMMENDED, URL where an end-user can view which applications currently have access to data and can make adjustments to these access rights.
        /// </summary>
        [JsonProperty("management_endpoint", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public Uri ManagementEndpoint { get; }

        /// <summary>
        /// RECOMMENDED, URL to a server’s introspection endpoint that can be used to validate a token.
        /// </summary>
        [JsonProperty("introspection_endpoint", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public Uri IntrospectionEndpoint { get; }

        /// <summary>
        /// RECOMMENDED, URL to a server’s revoke endpoint that can be used to revoke a token.
        /// </summary>
        [JsonProperty("revocation_endpoint", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public Uri RevocationEndpoint { get; }

        /// <summary>
        /// REQUIRED, Array of strings representing SMART capabilities (e.g., sso-openid-connect or launch-standalone) that the server supports.
        /// </summary>
        [JsonProperty("capabilities", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public ICollection<string> Capabilities { get; } = new List<string>();

        /// <summary>
        /// REQUIRED, Array of PKCE code challenge methods supported. The S256 method SHALL be included in this list, and the plain method SHALL NOT be included in this list.
        /// </summary>
        [JsonProperty("code_challenge_methods_supported", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public ICollection<string> CodeChallengeMethodsSupported { get; }
    }
}
