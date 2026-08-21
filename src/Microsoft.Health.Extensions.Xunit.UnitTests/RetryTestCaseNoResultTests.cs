// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Xunit;
using Xunit.Sdk;
using Xunit.v3;

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    /// <summary>
    /// Covers how <see cref="RetryTestCase"/> accounts for an attempt that published no result of
    /// its own, which happens when the run is cancelled or aborted underneath it.
    /// </summary>
    /// <remarks>
    /// This decision cannot be reached deterministically from a scenario asset, because it needs the
    /// runner to abandon an attempt mid-flight at an exact moment. The decision is therefore made by
    /// two pure helpers so that every combination can be pinned directly. Without these tests the
    /// branch that stops a never-reported test from counting as a pass is unprotected.
    /// </remarks>
    public class RetryTestCaseNoResultTests
    {
        /// <summary>
        /// A failure seen by the attempt that was cut short supersedes anything an earlier attempt
        /// deferred, so only the current attempt's failure is replayed. Replaying both would publish
        /// two results for one test.
        /// </summary>
        [Fact]
        public void GivenFailuresOnBothTheCurrentAndAnEarlierAttempt_WhenNothingWasReported_ThenOnlyTheCurrentAttemptIsReplayed()
        {
            NoResultOutcome outcome = RetryTestCase.DecideNoResultOutcome(
                currentAttemptObservedFailure: true,
                earlierAttemptObservedFailure: true);

            Assert.Equal(NoResultOutcome.ReplayCurrentAttempt, outcome);
        }

        /// <summary>
        /// A failure seen only by the attempt that was cut short is still a real observed failure.
        /// </summary>
        [Fact]
        public void GivenAFailureOnTheCurrentAttemptOnly_WhenNothingWasReported_ThenTheCurrentAttemptIsReplayed()
        {
            NoResultOutcome outcome = RetryTestCase.DecideNoResultOutcome(
                currentAttemptObservedFailure: true,
                earlierAttemptObservedFailure: false);

            Assert.Equal(NoResultOutcome.ReplayCurrentAttempt, outcome);
        }

        /// <summary>
        /// A failure deferred while waiting for a retry that then reported nothing must still reach
        /// the results, rather than being dropped along with the abandoned attempt.
        /// </summary>
        [Fact]
        public void GivenAFailureOnAnEarlierAttemptOnly_WhenNothingWasReported_ThenTheEarlierAttemptIsReplayed()
        {
            NoResultOutcome outcome = RetryTestCase.DecideNoResultOutcome(
                currentAttemptObservedFailure: false,
                earlierAttemptObservedFailure: true);

            Assert.Equal(NoResultOutcome.ReplayEarlierAttempt, outcome);
        }

        /// <summary>
        /// When no attempt ever saw a failure and none reported a result, there is nothing to replay.
        /// </summary>
        [Fact]
        public void GivenNoObservedFailure_WhenNothingWasReported_ThenNothingIsReplayed()
        {
            NoResultOutcome outcome = RetryTestCase.DecideNoResultOutcome(
                currentAttemptObservedFailure: false,
                earlierAttemptObservedFailure: false);

            Assert.Equal(NoResultOutcome.ReportNothing, outcome);
        }

        /// <summary>
        /// A test that no attempt ever reported must contribute nothing to the totals. Reporting one
        /// test with no failures is what previously turned an abandoned test into a silent pass.
        /// </summary>
        [Fact]
        public void GivenNothingToReplay_WhenTheSummaryIsBuilt_ThenTheTestContributesNothing()
        {
            RunSummary summary = RetryTestCase.CreateNoResultSummary(NoResultOutcome.ReportNothing);

            Assert.Equal(0, summary.Total);
            Assert.Equal(0, summary.Failed);
        }

        /// <summary>
        /// A failure replayed from the attempt that was cut short is a published result, so it must
        /// be counted as one failed test.
        /// </summary>
        [Fact]
        public void GivenTheCurrentAttemptIsReplayed_WhenTheSummaryIsBuilt_ThenTheTestCountsAsOneFailure()
        {
            RunSummary summary = RetryTestCase.CreateNoResultSummary(NoResultOutcome.ReplayCurrentAttempt);

            Assert.Equal(1, summary.Total);
            Assert.Equal(1, summary.Failed);
        }

        /// <summary>
        /// A failure replayed from an earlier attempt is equally a published result, so it counts the
        /// same way.
        /// </summary>
        [Fact]
        public void GivenAnEarlierAttemptIsReplayed_WhenTheSummaryIsBuilt_ThenTheTestCountsAsOneFailure()
        {
            RunSummary summary = RetryTestCase.CreateNoResultSummary(NoResultOutcome.ReplayEarlierAttempt);

            Assert.Equal(1, summary.Total);
            Assert.Equal(1, summary.Failed);
        }
    }
}
