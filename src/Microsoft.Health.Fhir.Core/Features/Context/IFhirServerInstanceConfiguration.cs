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
        /// Initializes the base URI of the instance configuration.
        /// This method is idempotent - only the first call will succeed in setting the value.
        /// </summary>
        /// <param name="baseUriString">The base URI string of the FHIR server.</param>
        /// <returns>True if the base URI is initialized (either by this call or a previous call); false if the URI is invalid.</returns>
        bool InitializeBaseUri(string baseUriString);
    }
}
