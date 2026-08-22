// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.SkipThenTeardownThrows
{
    /// <summary>
    /// A retrying test that fails, then on its second attempt skips itself and throws from teardown.
    /// Expected: 1 result, failed.
    /// </summary>
    /// <remarks>
    /// This is the narrowest corner of the retry state machine. Holding a skip so it cannot erase an
    /// earlier failure also holds every message that follows it, teardown's failure among them, which
    /// raised the question of whether one attempt could report both.
    /// <para>
    /// It does not: xUnit collects the skip and the teardown exception into one AggregateException and
    /// reports the attempt failed, so no skip is ever published and the abstention-holding path is not
    /// entered. The test is therefore retried as the genuine failure it is, and reported failed once.
    /// </para>
    /// </remarks>
    public class SkipThenTeardownThrowsTests : IDisposable
    {
        private static int _attempts;

        /// <summary>
        /// Throws from teardown on every attempt after the first, which is the attempt that skips.
        /// </summary>
        public void Dispose()
        {
            if (Volatile.Read(ref _attempts) > 1)
            {
                throw new InvalidOperationException("ASSET: teardown throws after the skip");
            }
        }

        /// <summary>
        /// Throws on the first attempt - a non-assertion exception, so it is retried under the
        /// default policy - and skips itself on the second, whose teardown then throws.
        /// </summary>
        [RetryFact(MaxRetries = 3, DelayBetweenRetriesMs = 10)]
        public void FailsThenSkipsWithThrowingTeardown()
        {
            if (Interlocked.Increment(ref _attempts) == 1)
            {
                throw new InvalidOperationException("ASSET: first attempt fails");
            }

            Assert.Skip("ASSET: skipped on the second attempt");
        }
    }
}
