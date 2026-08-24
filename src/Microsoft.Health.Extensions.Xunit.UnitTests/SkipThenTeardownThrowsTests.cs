// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    /// <summary>
    /// Pins what a retrying test reports when an attempt both abstains and fails in its teardown,
    /// after an earlier attempt had already failed.
    /// </summary>
    /// <remarks>
    /// Holding a skip so it cannot erase an earlier failure also holds every message that follows it,
    /// because a reporter finalizes a test at ITestFinished and anything forwarded afterwards is
    /// dropped. That raised the question of whether an attempt could arrive carrying both an
    /// abstention and a failure of its own, and be reported twice.
    /// <para>
    /// It cannot, and this records why rather than leaving the reasoning to be redone. xUnit collects
    /// the skip and the teardown exception into one AggregateException and publishes a single
    /// ITestFailed; no ITestSkipped is ever raised, so the abstention-holding path is not entered at
    /// all. That is xUnit's behaviour rather than this code's, which is exactly why it is worth
    /// pinning: were it to change, the retry machinery would start seeing a case it has no branch for.
    /// </para>
    /// </remarks>
    public class SkipThenTeardownThrowsTests
    {
        private const string ClassName = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.SkipThenTeardownThrows.SkipThenTeardownThrowsTests.";

        /// <summary>
        /// A test that fails, is retried, then skips itself while its teardown throws is reported
        /// failed exactly once - not skipped, not twice, and not absent.
        /// </summary>
        [Fact]
        public void GivenARetryingTestThatSkipsAndThrowsFromTeardown_WhenTheRunCompletes_ThenItIsReportedFailedOnce()
        {
            TestAssetRun run = TestAssetRunner.Run("SkipThenTeardownThrows");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string> { [ClassName + "FailsThenSkipsWithThrowingTeardown"] = "Failed" });
        }
    }
}
