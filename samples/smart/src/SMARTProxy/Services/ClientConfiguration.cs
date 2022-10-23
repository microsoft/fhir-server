// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.ObjectModel;

namespace SMARTProxy.Services
{
    public class ClientConfiguration
    {
        public ClientConfiguration(string clientId, string clientSecret, string jwksUri, string? allowedScopes = null)
        {
            ClientId = clientId;
            ClientSecret = clientSecret;
            JwksUri = new Uri(jwksUri);

            var scopeBuild = new List<string>();

            if (!string.IsNullOrEmpty(allowedScopes))
            {
                foreach (string scope in allowedScopes.Split(' '))
                {
                    scopeBuild.Add(scope);
                }
            }

            AllowedScopes = new ReadOnlyCollection<string>(scopeBuild);
        }

        public string ClientId { get; }

        public string ClientSecret { get; }

        public Uri JwksUri { get; }

        public IReadOnlyCollection<string> AllowedScopes { get; }
    }
}
