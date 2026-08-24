// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Xunit.Sdk;

namespace Xunit
{
    /// <summary>
    /// Provides legacy SkippableFact-compatible helpers for conditional skips.
    /// </summary>
    public static class Skip
    {
        /// <summary>
        /// Skips the current test when the condition is true.
        /// </summary>
        /// <param name="condition">The condition that triggers the skip.</param>
        /// <param name="reason">The reason for the skip.</param>
        public static void If(bool condition, string reason = null)
        {
            if (condition)
            {
                throw SkipException.ForSkip(reason ?? "Skipped");
            }
        }

        /// <summary>
        /// Skips the current test when the condition is false.
        /// </summary>
        /// <param name="condition">The condition that prevents the skip.</param>
        /// <param name="reason">The reason for the skip.</param>
        public static void IfNot(bool condition, string reason = null)
        {
            if (!condition)
            {
                throw SkipException.ForSkip(reason ?? "Skipped");
            }
        }
    }
}
