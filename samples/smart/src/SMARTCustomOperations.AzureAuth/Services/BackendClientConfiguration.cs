// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace SMARTCustomOperations.AzureAuth.Services
{
    public class BackendClientConfiguration
    {
        public BackendClientConfiguration()
        {
            ClientId = default!;
            ClientSecret = default!;
        }

        public BackendClientConfiguration(string clientId, string clientSecret, string jwksUri)
        {
            ClientId = clientId;
            ClientSecret = clientSecret;

            if (jwksUri is not null)
            {
                JwksUri = new Uri(jwksUri);
            }
        }

        public string ClientId { get; }

        public string ClientSecret { get; }

        public Uri? JwksUri { get; }
    }
}
