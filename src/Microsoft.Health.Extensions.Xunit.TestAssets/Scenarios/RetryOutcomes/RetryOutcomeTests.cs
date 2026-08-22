// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.RetryOutcomes
{
    /// <summary>
    /// The full <see cref="RetryFactAttribute"/> outcome matrix, run without cancellation.
    /// Expected: 7 results, 3 failed, 3 passed and 1 skipped, with exactly one result per test.
    /// </summary>
    public class RetryOutcomeTests
    {
        private static int _flakyAttempts;
        private static int _clampedAttempts;
        private static int _clampedDelayAttempts;

        /// <summary>
        /// An assertion failure with retries disabled must be reported once and not retried.
        /// This is the case that originally disappeared from the results entirely, because the
        /// failure was deferred for a retry that then never happened.
        /// </summary>
        [RetryFact(MaxRetries = 3, DelayBetweenRetriesMs = 10, RetryOnAssertionFailure = false)]
        public void NonRetriableAssertionFailure_IsReportedOnce()
        {
            Assert.Fail("ASSET: non-retriable assertion failure");
        }

        /// <summary>
        /// A test that fails every attempt must be reported as failed once, after the retries
        /// are exhausted, rather than losing the final attempt's failure.
        /// </summary>
        [RetryFact(MaxRetries = 3, DelayBetweenRetriesMs = 10, RetryOnAssertionFailure = true)]
        public void ExhaustedRetries_IsReportedFailedOnce()
        {
            Assert.Fail("ASSET: fails on every attempt");
        }

        /// <summary>
        /// A test that fails once then succeeds must report a single pass, with the superseded
        /// failure discarded rather than also reported.
        /// </summary>
        [RetryFact(MaxRetries = 3, DelayBetweenRetriesMs = 10, RetryOnAssertionFailure = true)]
        public void FlakyThenPasses_IsReportedPassedOnce()
        {
            if (Interlocked.Increment(ref _flakyAttempts) == 1)
            {
                Assert.Fail("ASSET: first attempt fails, second succeeds");
            }
        }

        /// <summary>
        /// Non-assertion exceptions are retried regardless of RetryOnAssertionFailure, and must
        /// still be reported once the attempts are exhausted.
        /// </summary>
        [RetryFact(MaxRetries = 2, DelayBetweenRetriesMs = 10)]
        public void NonAssertionException_IsReportedFailedOnce()
        {
            throw new InvalidOperationException("ASSET: transient-style exception");
        }

        /// <summary>
        /// A plain passing test must be reported as passed.
        /// </summary>
        [RetryFact(MaxRetries = 3, DelayBetweenRetriesMs = 10)]
        public void AlwaysPasses_IsReportedPassed()
        {
            Assert.True(true);
        }

        /// <summary>
        /// A test that skips itself must be reported as skipped, and must not be retried on the way
        /// there: a skip is not a failure, so the attempt loop has to stop at it rather than run the
        /// test again and report the last attempt as a pass.
        /// </summary>
        [RetryFact(MaxRetries = 3, DelayBetweenRetriesMs = 10)]
        public void SkippedAtRunTime_IsReportedSkipped()
        {
            Assert.Skip("ASSET: skipped at run time");
        }

        /// <summary>
        /// A MaxRetries below one would skip the attempt loop and report nothing, so it is clamped
        /// up to one and the test runs exactly once. This test cannot say anything about the delay:
        /// clamping MaxRetries to one means no retry, and the delay is only ever reached before one.
        /// </summary>
        [RetryFact(MaxRetries = 0, DelayBetweenRetriesMs = 10, RetryOnAssertionFailure = true)]
        public void ClampedRetryConfiguration_RunsExactlyOnce()
        {
            Assert.Equal(1, Interlocked.Increment(ref _clampedAttempts));
        }

        /// <summary>
        /// A negative delay would make Task.Delay throw, which would lose the retry this test needs
        /// to pass. Reaching the delay at all takes a failed first attempt, so this fails once and
        /// then succeeds: a passing result is the statement that the clamp held.
        /// </summary>
        [RetryFact(MaxRetries = 2, DelayBetweenRetriesMs = -500, RetryOnAssertionFailure = true)]
        public void ClampedNegativeDelay_StillRetries()
        {
            if (Interlocked.Increment(ref _clampedDelayAttempts) == 1)
            {
                Assert.Fail("ASSET: failing so the retry delay is reached");
            }
        }
    }
}
