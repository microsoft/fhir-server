// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    /// <summary>
    /// Covers a discovery failure that is shaped like cancellation without the run having been
    /// cancelled.
    /// </summary>
    /// <remarks>
    /// A cancelled run is rightly not reported as a fault, because a class that happened to be mid
    /// expansion when Ctrl+C arrived has nothing wrong with it. That exclusion is only safe while it
    /// recognises cancellation by the run actually having been cancelled. Recognising it by the
    /// exception's type instead hands every class a way out of being reported: expansion runs the
    /// attributes declared on the class, and an attribute that awaits anything with a timeout throws
    /// <see cref="System.Threading.Tasks.TaskCanceledException"/> of its own accord. Treated as
    /// cancellation, that drops the class and leaves the run green with its tests missing, which is
    /// the one outcome reporting discovery faults exists to prevent.
    /// </remarks>
    public class CancellationLookalikeFaultTests
    {
        private const string ErrorCaseName = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.CancellationLookalikeFault.CancellationLookalikeFaultTests.NeverRuns (fixture argument set discovery: Sql)";

        /// <summary>
        /// The failure is reported as a test case and the run fails, rather than the class being
        /// dropped in the belief that the run was cancelled.
        /// </summary>
        [Fact]
        public void GivenAnAttributeThatFailsLikeACancellation_WhenNothingWasCancelled_ThenTheFaultIsReported()
        {
            TestAssetRun run = TestAssetRunner.Run("CancellationLookalikeFault");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [ErrorCaseName] = "Failed",
                });

            Assert.NotEqual(0, run.ExitCode);
        }

        /// <summary>
        /// The cause has to survive into the output, or whoever reads the results is told a test
        /// failed without being told the class never expanded.
        /// </summary>
        [Fact]
        public void GivenAnAttributeThatFailsLikeACancellation_WhenItIsDiscovered_ThenTheCauseIsWrittenToTheOutput()
        {
            TestAssetRun run = TestAssetRunner.Run("CancellationLookalikeFault");

            Assert.Contains("This argument set attribute gave up waiting.", run.Output, StringComparison.Ordinal);
        }
    }
}
