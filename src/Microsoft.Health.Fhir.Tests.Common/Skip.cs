// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Tests.Common
{
    /// <summary>
    /// Compatibility shim to preserve Skip.If/Skip.IfNot usage across test projects.
    /// </summary>
    public static class Skip
    {
        /// <summary>
        /// Skips the current test if the specified condition is true.
        /// </summary>
        /// <param name="condition">If true, the test will be skipped.</param>
        /// <param name="reason">The reason for skipping the test.</param>
        public static void If(bool condition, string reason = "Condition was true")
        {
            SkipCompatibility.If(condition, reason);
        }

        /// <summary>
        /// Skips the current test if the specified condition is false.
        /// </summary>
        /// <param name="condition">If false, the test will be skipped.</param>
        /// <param name="reason">The reason for skipping the test.</param>
        public static void IfNot(bool condition, string reason = "Condition was false")
        {
            SkipCompatibility.IfNot(condition, reason);
        }
    }
}
