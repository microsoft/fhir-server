// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.CancelledDuringRetryDelay
{
    /// <summary>
    /// A retrying test whose failure is deferred must still be reported when the run is
    /// cancelled before a later attempt can supersede it.
    /// </summary>
    /// <remarks>
    /// Run this namespace with --stop-on-fail on, so the sibling collection cancels the run
    /// while this test is waiting to retry. Before the deferred failure was carried across
    /// attempts, this test vanished from the results entirely -- neither passed, failed nor
    /// skipped -- and the run reported success.
    /// </remarks>
    [Collection("DeferredFailure")]
    public class DeferredFailureTests
    {
        /// <summary>
        /// Fails on every attempt. The long delay guarantees the run is cancelled while this
        /// test is waiting to retry, so the deferred first-attempt failure is the only record
        /// that it ever ran.
        /// </summary>
        [RetryFact(MaxRetries = 3, DelayBetweenRetriesMs = 30000, RetryOnAssertionFailure = true)]
        public void FailureSurvivesCancellationDuringRetryDelay()
        {
            // Tell the sibling collection it may now cancel the run: the retry delay this scenario
            // needs it cancelled during begins the moment this attempt fails.
            RetryDelayHandshake.AnnounceAttemptFailing();

            Assert.Fail("ASSET: failure that must survive cancellation");
        }
    }
}
