// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.BoundedParallelism
{
    /// <summary>
    /// Counts how many tests of this scenario are inside their body at the same time.
    /// </summary>
    public static class ConcurrencyTracker
    {
        private static int _current;

        /// <summary>
        /// Records entry into a test body.
        /// </summary>
        /// <returns>The number of tests inside a body once this one has entered.</returns>
        public static int Enter() => Interlocked.Increment(ref _current);

        /// <summary>
        /// Records that a test body has been left.
        /// </summary>
        public static void Exit() => Interlocked.Decrement(ref _current);
    }
}
