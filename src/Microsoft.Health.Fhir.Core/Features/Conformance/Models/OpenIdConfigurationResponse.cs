// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Conformance.Models
{
    /// <summary>
    /// This class represents the minimum required properties of the OpenID Connect discovery document used in the capability statement.
    /// </summary>
    public class OpenIdConfigurationResponse
    {
        public OpenIdConfigurationResponse(
            Uri authorizationEndpoint,
            Uri tokenEndpoint)
        {
            AuthorizationEndpoint = authorizationEndpoint;
            TokenEndpoint = tokenEndpoint;
        }

        /// <summary>
        /// REQUIRED, URL to the OAuth2 authorization endpoint.
        /// </summary>
        [JsonProperty("authorization_endpoint", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public Uri AuthorizationEndpoint { get; }

        /// <summary>
        /// REQUIRED, URL to the OAuth2 token endpoint.
        /// </summary>
        [JsonProperty("token_endpoint", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public Uri TokenEndpoint { get; }
    }
}
