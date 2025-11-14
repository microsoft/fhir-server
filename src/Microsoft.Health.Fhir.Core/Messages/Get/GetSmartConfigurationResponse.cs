// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Messages.Get
{
    public class GetSmartConfigurationResponse
    {
        public GetSmartConfigurationResponse(Uri authorizationEndpoint, Uri tokenEndpoint, ICollection<string> capabilities)
        {
            EnsureArg.IsNotNull(authorizationEndpoint, nameof(authorizationEndpoint));
            EnsureArg.IsNotNull(tokenEndpoint, nameof(tokenEndpoint));
            EnsureArg.IsNotNull(capabilities, nameof(capabilities));

            AuthorizationEndpoint = authorizationEndpoint;
            TokenEndpoint = tokenEndpoint;
            Capabilities = capabilities;
        }

        public GetSmartConfigurationResponse(
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

        public Uri AuthorizationEndpoint { get; }

        public Uri TokenEndpoint { get; }

        public ICollection<string> Capabilities { get; }

        public ICollection<string> ScopesSupported { get; }

        public ICollection<string> CodeChallengeMethodsSupported { get; }

        public ICollection<string> GrantTypesSupported { get; }

        public ICollection<string> TokenEndpointAuthMethodsSupported { get; }

        public ICollection<string> ResponseTypesSupported { get; }

        public string IntrospectionEndpoint { get; }

        public string ManagementEndpoint { get; }

        public string RevocationEndpoint { get; }
    }
}
