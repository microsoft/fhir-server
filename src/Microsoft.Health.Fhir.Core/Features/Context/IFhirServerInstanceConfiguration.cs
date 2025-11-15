// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Features.Context
{
    /// <summary>
    /// Provides global, thread-safe access to FHIR server instance configuration
    /// that persists across HTTP requests and is available to background tasks.
    /// </summary>
    public interface IFhirServerInstanceConfiguration
    {
        /// <summary>
        /// Gets the base URI of the FHIR server instance.
        /// </summary>
        Uri BaseUri { get; }

        /// <summary>
        /// Gets the vanity URI of the FHIR server instance.
        /// If not explicitly set, defaults to the base URI.
        /// </summary>
        Uri VanityUrl { get; }

        /// <summary>
        /// Gets a value indicating whether the instance configuration has been initialized.
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>
        /// Initializes the instance configuration with server metadata.
        /// This method is idempotent - only the first call will succeed in setting values.
        /// </summary>
        /// <param name="baseUriString">The base URI string of the FHIR server.</param>
        /// <param name="vanityUrlString">Optional vanity URL string of the FHIR server. If not provided, defaults to baseUriString.</param>
        void Initialize(string baseUriString, string vanityUrlString = null);
    }
}
