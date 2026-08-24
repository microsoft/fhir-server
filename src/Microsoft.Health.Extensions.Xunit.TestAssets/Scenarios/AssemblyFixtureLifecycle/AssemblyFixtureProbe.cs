// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.AssemblyFixtureLifecycle
{
    /// <summary>
    /// Records that the assembly fixture was constructed, so a test can tell whether that happened
    /// before it ran.
    /// </summary>
    public static class AssemblyFixtureProbe
    {
        private static volatile bool _constructed;

        /// <summary>
        /// Gets or sets a value indicating whether the assembly fixture has been constructed.
        /// </summary>
        public static bool Constructed
        {
            get => _constructed;
            set => _constructed = value;
        }
    }
}
