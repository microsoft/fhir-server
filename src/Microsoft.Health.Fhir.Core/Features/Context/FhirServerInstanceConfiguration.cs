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
        private int _baseUriInitialized;

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
        /// Initializes the base URI of the instance configuration independently.
        /// This method is idempotent and thread-safe - only the first caller will succeed in setting the value.
        /// </summary>
        /// <param name="baseUriString">The base URI string of the FHIR server.</param>
        /// <returns>True if the base URI is initialized (either by this call or a previous call); false if the URI is invalid.</returns>
        public bool InitializeBaseUri(string baseUriString)
        {
            EnsureArg.IsNotNullOrWhiteSpace(baseUriString, nameof(baseUriString));

            if (Uri.TryCreate(baseUriString, UriKind.Absolute, out Uri baseUri) &&
                Interlocked.CompareExchange(ref _baseUriInitialized, 1, 0) == 0)
            {
                // We won the race - set the value
                BaseUri = baseUri;
            }

            return _baseUriInitialized != 0;
        }
    }
}
