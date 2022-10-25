// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace SMARTProxy.Services
{
    public class BackendClientConfiguration
    {
        public BackendClientConfiguration(string clientId, string clientSecret, string jwksUri, string? allowedScopes = null)
        {
            ClientId = clientId;
            ClientSecret = clientSecret;
            JwksUri = new Uri(jwksUri);

            var scopeBuild = new List<string>();
        }

        public string ClientId { get; }

        public string ClientSecret { get; }

        public Uri JwksUri { get; }
    }
}
