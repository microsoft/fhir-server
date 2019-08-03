// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Utilities
{
    /// <summary>
    /// Provides functionality to retrieve caller IP address.
    /// </summary>
    public interface ICallerIpAddressRetriever
    {
        /// <summary>
        /// Gets the caller IP address.
        /// </summary>
        string CallerIpAddress { get; }
    }
}
