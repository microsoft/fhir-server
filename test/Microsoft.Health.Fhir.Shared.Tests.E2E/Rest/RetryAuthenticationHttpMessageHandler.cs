// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    /// <summary>
    /// A delegating handler that retries requests on 401 Unauthorized responses.
    /// When a 401 is received, it invalidates the cached token and retries with exponential backoff.
    /// This handles transient authentication failures during E2E test initialization.
    /// </summary>
    public class RetryAuthenticationHttpMessageHandler : DelegatingHandler
    {
        private readonly RetryableCredentialProvider _credentialProvider;
        private readonly int _maxRetries;
        private readonly TimeSpan _baseDelay;

        /// <summary>
        /// Initializes a new instance of the <see cref="RetryAuthenticationHttpMessageHandler"/> class.
        /// </summary>
        /// <param name="credentialProvider">The credential provider that supports token invalidation.</param>
        /// <param name="innerHandler">The inner handler to delegate to.</param>
        /// <param name="maxRetries">Maximum number of retry attempts (default: 3).</param>
        /// <param name="baseDelay">Base delay for exponential backoff (default: 2 seconds).</param>
        public RetryAuthenticationHttpMessageHandler(
            RetryableCredentialProvider credentialProvider,
            HttpMessageHandler innerHandler,
            int maxRetries = 3,
            TimeSpan? baseDelay = null)
            : base(innerHandler)
        {
            _credentialProvider = credentialProvider ?? throw new ArgumentNullException(nameof(credentialProvider));
            _maxRetries = maxRetries;
            _baseDelay = baseDelay ?? TimeSpan.FromSeconds(2);
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            HttpResponseMessage response = null;
            Exception lastException = null;
            int attempt = 0;

            // First attempt
            try
            {
                response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsTransientTokenException(ex))
            {
                lastException = ex;
                Console.WriteLine($"[RetryAuthenticationHttpMessageHandler] Token acquisition failed for {request.Method} {request.RequestUri}: {ex.Message}");
            }

            // Retry loop for 401 responses OR token acquisition failures
            while (attempt < _maxRetries && (response?.StatusCode == HttpStatusCode.Unauthorized || (response == null && lastException != null)))
            {
                attempt++;

                // Calculate exponential backoff delay: 2^attempt seconds (2s, 4s, 8s)
                TimeSpan delay = TimeSpan.FromTicks(_baseDelay.Ticks * (long)Math.Pow(2, attempt - 1));

                string reason = response?.StatusCode == HttpStatusCode.Unauthorized
                    ? "401 Unauthorized"
                    : $"Token acquisition exception: {lastException?.GetType().Name}";

                Console.WriteLine($"[RetryAuthenticationHttpMessageHandler] {reason} for {request.Method} {request.RequestUri}. " +
                    $"Attempt {attempt}/{_maxRetries}. Invalidating token and retrying after {delay.TotalSeconds}s...");

                // Dispose the previous response before retrying
                response?.Dispose();
                response = null;
                lastException = null;

                // Invalidate the cached token to force re-acquisition
                _credentialProvider.InvalidateToken();

                // Wait with exponential backoff
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

                // Clone the request since HttpRequestMessage can only be sent once
                using var clonedRequest = await CloneRequestAsync(request).ConfigureAwait(false);

                // Retry the request - the AuthenticationHttpMessageHandler will get a fresh token
                try
                {
                    response = await base.SendAsync(clonedRequest, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (IsTransientTokenException(ex))
                {
                    lastException = ex;
                    Console.WriteLine($"[RetryAuthenticationHttpMessageHandler] Token acquisition failed on retry {attempt}: {ex.Message}");
                }
            }

            // If we still have an exception after all retries, throw it
            if (response == null && lastException != null)
            {
                Console.WriteLine($"[RetryAuthenticationHttpMessageHandler] FATAL: Token acquisition failed after {_maxRetries} retries for {request.Method} {request.RequestUri}. " +
                    $"Last exception: {lastException.Message}");
                throw lastException;
            }

            if (response?.StatusCode == HttpStatusCode.Unauthorized)
            {
                Console.WriteLine($"[RetryAuthenticationHttpMessageHandler] FATAL: Still receiving 401 after {_maxRetries} retries for {request.Method} {request.RequestUri}. " +
                    "Authentication is not working. The test run will fail.");
            }

            return response;
        }

        /// <summary>
        /// Determines if an exception is a transient token acquisition failure that should be retried.
        /// </summary>
        private static bool IsTransientTokenException(Exception ex)
        {
            // InvalidOperationException from RetryableCredentialProvider when token is null
            if (ex is InvalidOperationException && ex.Message.Contains("token", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // ArgumentNullException from JWT parsing when token is null (shouldn't happen after our fix, but just in case)
            if (ex is ArgumentNullException ane && ane.ParamName == "jwtEncodedString")
            {
                return true;
            }

            // Check inner exceptions
            if (ex.InnerException != null)
            {
                return IsTransientTokenException(ex.InnerException);
            }

            return false;
        }

        private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri)
            {
                Version = request.Version,
            };

            // Copy headers (except Authorization which will be re-added by AuthenticationHttpMessageHandler)
            foreach (var header in request.Headers)
            {
                if (!string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase))
                {
                    clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            // Copy content if present
            if (request.Content != null)
            {
                var contentBytes = await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                clone.Content = new ByteArrayContent(contentBytes);

                // Copy content headers
                foreach (var header in request.Content.Headers)
                {
                    clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            // Copy properties (using the Properties dictionary which works across all .NET versions)
#pragma warning disable CS0618 // Properties is obsolete in .NET 5+ but still works
            foreach (var property in request.Properties)
            {
                clone.Properties.Add(property);
            }
#pragma warning restore CS0618

            return clone;
        }
    }
}
