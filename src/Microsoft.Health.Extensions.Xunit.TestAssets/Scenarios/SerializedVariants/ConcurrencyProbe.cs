// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.SerializedVariants
{
    /// <summary>
    /// Records whether any two tests that used it were ever inside their bodies at the same time.
    /// </summary>
    internal static class ConcurrencyProbe
    {
        private static int _active;
        private static int _observedOverlap;

        /// <summary>
        /// Gets a value indicating whether two tests were ever active at once.
        /// </summary>
        internal static bool ObservedOverlap => Volatile.Read(ref _observedOverlap) != 0;

        /// <summary>
        /// Marks the caller as active for a fixed window, so that a concurrent caller overlaps it.
        /// </summary>
        /// <remarks>
        /// The window has to be long enough that a genuinely parallel run overlaps rather than
        /// merely interleaving between the increment and the decrement. Tests in one xUnit
        /// collection run one after another, so the window costs a serialized run only its own
        /// duration and never makes the assertion flaky in the direction that matters: a serial
        /// run cannot observe an overlap however long the window is.
        /// </remarks>
        internal static void Occupy()
        {
            if (Interlocked.Increment(ref _active) > 1)
            {
                Interlocked.Exchange(ref _observedOverlap, 1);
            }

            try
            {
                Thread.Sleep(250);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }
}
