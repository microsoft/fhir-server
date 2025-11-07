// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Context
{
    /// <summary>
    /// Provides global, thread-safe access to FHIR server instance configuration (base URI, etc.)
    /// that persists across requests and is available to background tasks that execute outside the HTTP request context.
    ///
    /// This is a singleton service that is populated on the first HTTP request via the middleware and then remains
    /// available for background operations like reindexing that don't have access to the RequestContextAccessor.
    ///
    /// This design ensures:
    /// - Minimal performance impact (lazy-initialization pattern)
    /// - Thread-safe access (Interlocked for simple properties)
    /// - No per-request overhead (values are captured once and reused)
    /// - Available to background services and job processing
    /// </summary>
    public class FhirServerInstanceConfiguration : IFhirServerInstanceConfiguration
    {
        private Uri _cachedBaseUri;
        private Uri _cachedVanityUrl;
        private int _initialized;

        /// <summary>
        /// Gets the base URI of the FHIR server instance.
        /// Populated on first HTTP request and cached for the lifetime of the application.
        /// </summary>
        public Uri BaseUri
        {
            get => _cachedBaseUri;
            private set => _cachedBaseUri = value;
        }

        /// <summary>
        /// Gets the vanity URI of the FHIR server instance.
        /// Returns null if not explicitly set.
        /// Populated on first HTTP request and cached for the lifetime of the application.
        /// </summary>
        public Uri VanityUrl
        {
            get => _cachedVanityUrl;
            private set => _cachedVanityUrl = value;
        }

        /// <summary>
        /// Gets a value indicating whether the instance configuration has been initialized with server metadata.
        /// </summary>
        public bool IsInitialized => _initialized == 1;

        /// <summary>
        /// Initializes the instance configuration with base URI and optional vanity URL strings.
        /// This method is idempotent and thread-safe - only the first caller will succeed in setting values.
        /// Subsequent calls will be no-ops.
        /// </summary>
        /// <param name="baseUriString">The base URI string of the FHIR server.</param>
        /// <param name="vanityUrlString">Optional vanity URI string of the FHIR server. If not provided, defaults to baseUriString.</param>
        public void Initialize(string baseUriString, string vanityUrlString = null)
        {
            EnsureArg.IsNotNullOrWhiteSpace(baseUriString, nameof(baseUriString));

            if (Uri.TryCreate(baseUriString, UriKind.Absolute, out Uri baseUri))
            {
                // Use Interlocked.CompareExchange to ensure only one thread successfully initializes
                if (Interlocked.CompareExchange(ref _initialized, 1, 0) == 0)
                {
                    // We won the race - set the values
                    BaseUri = baseUri;

                    // If vanityUrlString is provided and valid, set it
                    if (!string.IsNullOrWhiteSpace(vanityUrlString) &&
                        Uri.TryCreate(vanityUrlString, UriKind.Absolute, out Uri vanityUrl))
                    {
                        VanityUrl = vanityUrl;
                    }
                }

                // If _initialized was already 1, another thread beat us to it, so we do nothing
            }
        }

        /// <summary>
        /// For testing purposes only - resets the configuration to an uninitialized state.
        /// </summary>
        internal void ResetForTesting()
        {
            _cachedBaseUri = null;
            _cachedVanityUrl = null;
            Interlocked.Exchange(ref _initialized, 0);
        }
    }
}
