// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Client.Authentication;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    /// <summary>
    /// A wrapper around an <see cref="ICredentialProvider"/> that adds the ability to invalidate the cached token.
    /// This is used to handle transient 401 errors during E2E test initialization by forcing a fresh token acquisition.
    /// </summary>
    public class RetryableCredentialProvider : ICredentialProvider
    {
        private readonly ICredentialProvider _innerProvider;
        private readonly object _lock = new object();
        private string _cachedToken;
        private DateTime _tokenExpiration = DateTime.MinValue;
        private readonly TimeSpan _tokenExpirationBuffer = TimeSpan.FromMinutes(5);

        public RetryableCredentialProvider(ICredentialProvider innerProvider)
        {
            _innerProvider = innerProvider ?? throw new ArgumentNullException(nameof(innerProvider));
        }

        /// <summary>
        /// Gets the bearer token, using a cached value if still valid or acquiring a new one from the inner provider.
        /// </summary>
        public async Task<string> GetBearerTokenAsync(CancellationToken cancellationToken)
        {
            // Check if we have a valid cached token
            lock (_lock)
            {
                if (!string.IsNullOrEmpty(_cachedToken) && DateTime.UtcNow < _tokenExpiration - _tokenExpirationBuffer)
                {
                    return _cachedToken;
                }
            }

            // Acquire a new token from the inner provider
            string token = await _innerProvider.GetBearerTokenAsync(cancellationToken).ConfigureAwait(false);

            // Handle null/empty token - this can happen transiently during AAD propagation delays
            if (string.IsNullOrEmpty(token))
            {
                Console.WriteLine("[RetryableCredentialProvider] Inner provider returned null/empty token. This may be a transient AAD issue.");
                throw new InvalidOperationException("Token acquisition returned null or empty token. This may be a transient authentication issue.");
            }

            // Cache the token and parse its expiration
            lock (_lock)
            {
                _cachedToken = token;
                try
                {
                    var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                    var jwtToken = handler.ReadJwtToken(token);
                    _tokenExpiration = jwtToken.ValidTo;
                }
                catch (Exception ex)
                {
                    // If we can't parse the token, assume a short expiration
                    Console.WriteLine($"[RetryableCredentialProvider] Could not parse JWT token expiration: {ex.Message}. Using default 30 minute expiration.");
                    _tokenExpiration = DateTime.UtcNow.AddMinutes(30);
                }
            }

            return token;
        }

        /// <summary>
        /// Invalidates the cached token, forcing the next call to <see cref="GetBearerTokenAsync"/> to acquire a fresh token.
        /// </summary>
        public void InvalidateToken()
        {
            lock (_lock)
            {
                Console.WriteLine($"[RetryableCredentialProvider] Invalidating cached token. Previous expiration: {_tokenExpiration:O}");
                _cachedToken = null;
                _tokenExpiration = DateTime.MinValue;
            }
        }
    }
}
