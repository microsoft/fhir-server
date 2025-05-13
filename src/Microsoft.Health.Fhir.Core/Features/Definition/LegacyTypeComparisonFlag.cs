// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Definition
{
    /// <summary>
    /// Provides conditional compilation flags for legacy type comparison behavior.
    /// </summary>
    public static class LegacyTypeComparisonFlag
    {
        /// <summary>
        /// When defined, uses the direct type comparison (r.Type == ResourceType.X) instead of string-based comparison.
        /// This can be set at the project level in the PropertyGroup section:
        /// <DefineConstants>USE_LEGACY_TYPE_COMPARISON;$(DefineConstants)</DefineConstants>
        /// </summary>
        public const bool UseLegacyTypeComparison =
#if USE_LEGACY_TYPE_COMPARISON
            true;
#else
            false;
#endif
    }
}
