// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Sdk
{
    /// <summary>
    /// Guards known compatibility fallbacks between SDK implementations.
    /// </summary>
    public interface ISdkFallbackGuard
    {
        /// <summary>
        /// Records or rejects a fallback to Firely based on the configured SDK mode.
        /// </summary>
        /// <param name="surface">The feature surface using the fallback.</param>
        /// <param name="reason">The reason the fallback is required.</param>
        void FirelyFallback(string surface, string reason);

        /// <summary>
        /// Records or rejects a fallback to Ignixa based on the configured SDK mode.
        /// </summary>
        /// <param name="surface">The feature surface using the fallback.</param>
        /// <param name="reason">The reason the fallback is required.</param>
        void IgnixaFallback(string surface, string reason);
    }
}
