// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Xunit;

namespace Microsoft.Health.Fhir.Tests.Common
{
    /// <summary>
    /// Compatibility shim for Xunit.SkippableFact migration to xUnit v3.
    /// In xUnit v3, dynamic skipping is done via Assert.Skip() instead of Skip.If()/Skip.IfNot().
    /// </summary>
    public static class SkipCompatibility
    {
        /// <summary>
        /// Skips the current test if the specified condition is true.
        /// </summary>
        /// <param name="condition">If true, the test will be skipped.</param>
        /// <param name="reason">The reason for skipping the test.</param>
        public static void If(bool condition, string reason = "Condition was true")
        {
            if (condition)
            {
                Assert.Skip(reason);
            }
        }

        /// <summary>
        /// Skips the current test if the specified condition is false.
        /// </summary>
        /// <param name="condition">If false, the test will be skipped.</param>
        /// <param name="reason">The reason for skipping the test.</param>
        public static void IfNot(bool condition, string reason = "Condition was false")
        {
            if (!condition)
            {
                Assert.Skip(reason);
            }
        }
    }
}
