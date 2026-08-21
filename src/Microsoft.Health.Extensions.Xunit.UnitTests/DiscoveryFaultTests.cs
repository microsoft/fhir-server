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
    /// Verifies that a class whose fixture argument sets cannot be expanded is reported as a failure
    /// rather than dropped from the run.
    /// </summary>
    public class DiscoveryFaultTests
    {
        private const string SqlErrorCaseName = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.DiscoveryFault.DiscoveryFaultTests.NeverRuns (fixture argument set discovery: Sql)";
        private const string CosmosErrorCaseName = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.DiscoveryFault.DiscoveryFaultTests.NeverRuns (fixture argument set discovery: Cosmos)";

        /// <summary>
        /// xUnit reports an exception thrown out of discovery only as a diagnostic message, which is
        /// suppressed unless the run was started with <c>--xunit-diagnostics</c>, and carries on
        /// without the class. A run containing any other healthy class therefore still reports
        /// success, so a broken expansion is indistinguishable from a class that has no tests. The
        /// discoverer turns the fault into a failing test case instead, which puts it in the results
        /// and in the exit code.
        /// </summary>
        [Fact]
        public void GivenAClassThatCannotBeExpanded_WhenItIsDiscovered_ThenTheFaultIsReportedAsAFailedTest()
        {
            TestAssetRun run = TestAssetRunner.Run("DiscoveryFault");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [SqlErrorCaseName] = "Failed",
                    [CosmosErrorCaseName] = "Failed",
                });

            Assert.NotEqual(0, run.ExitCode);
        }

        /// <summary>
        /// The console line the discoverer writes is where the original exception survives in full,
        /// including the stack that points at the line which threw. Without it a fault would be
        /// reported with no way to find out what it was.
        /// </summary>
        [Fact]
        public void GivenAClassThatCannotBeExpanded_WhenItIsDiscovered_ThenTheCauseIsWrittenToTheOutput()
        {
            TestAssetRun run = TestAssetRunner.Run("DiscoveryFault");

            Assert.Contains("[FixtureArgumentSets] ERROR", run.Output, StringComparison.Ordinal);
            Assert.Contains("IndexOutOfRangeException", run.Output, StringComparison.Ordinal);
        }

        /// <summary>
        /// The failure the runner reports has to carry the cause itself, not just the console. A
        /// fault case stands in for the variants of a class that never got built, and those variants
        /// are how the executor knows which argument to hand the class fixture; a case that does not
        /// say which one it stands for leaves the executor unable to build the fixture, and the
        /// report then shows that fixture failure instead of the discovery error behind it. Anyone
        /// reading the results would be sent to look at a fixture that is not the problem. This pins
        /// the reported message to the real exception so that cannot come back.
        /// </summary>
        [Fact]
        public void GivenAClassThatCannotBeExpanded_WhenItIsDiscovered_ThenTheReportedFailureCarriesTheOriginalException()
        {
            TestAssetRun run = TestAssetRunner.Run("DiscoveryFault");

            foreach (string caseName in new[] { SqlErrorCaseName, CosmosErrorCaseName })
            {
                string reported = ExtractReportedFailure(run.Output, caseName);

                Assert.Contains("IndexOutOfRangeException", reported, StringComparison.Ordinal);
                Assert.DoesNotContain("had multiple values", reported, StringComparison.Ordinal);
            }
        }

        /// <summary>
        /// Reporting the fault as a test is only worth anything if the legs that would have run the
        /// class still select it. Some CI legs pick their tests with a positive trait filter, and the
        /// variants this class never produced are what would have carried those traits, so a fault
        /// case with no traits of its own is filtered out and the leg passes with the class silently
        /// missing - which is the failure this whole mechanism exists to prevent, only harder to see.
        /// </summary>
        [Fact]
        public void GivenAClassThatCannotBeExpanded_WhenTestsAreSelectedByArgumentSetTrait_ThenTheFaultIsStillReported()
        {
            TestAssetRun run = TestAssetRunner.Run("DiscoveryFault", filterTrait: "AssetDataStore=Sql");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [SqlErrorCaseName] = "Failed",
                });

            Assert.NotEqual(0, run.ExitCode);
        }

        /// <summary>
        /// The repository's integration legs select their tests by <em>excluding</em> the other data
        /// store, and a trait exclusion drops a case when any of its values under that trait matches.
        /// A single fault case declaring every value the class asked for would therefore be dropped by
        /// the SQL leg for holding the Cosmos value and by the Cosmos leg for holding the SQL one, so
        /// the fault would reach neither and both legs would stay green with the class missing. One
        /// case per combination is what stops that: excluding one value leaves the others reported.
        /// </summary>
        [Fact]
        public void GivenAClassThatCannotBeExpanded_WhenTestsAreSelectedByExcludingAnArgumentSetTrait_ThenTheFaultIsStillReported()
        {
            TestAssetRun run = TestAssetRunner.Run("DiscoveryFault", filterNotTrait: "AssetDataStore=Sql");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [CosmosErrorCaseName] = "Failed",
                });

            Assert.NotEqual(0, run.ExitCode);
        }

        /// <summary>
        /// Reads back what the runner reported for one failed case, which is the block it prints
        /// after the case's own <c>failed</c> line and before whatever it prints next.
        /// </summary>
        /// <remarks>
        /// The message has to be taken from the console rather than the TRX report, because the TRX
        /// writer records no error information for these cases. Searching the whole output instead
        /// would prove nothing: the discoverer's own diagnostic line names the same exception, so an
        /// assertion over all of it would pass even with the reported failure saying something else
        /// entirely.
        /// </remarks>
        /// <param name="output">The full console output of the run.</param>
        /// <param name="caseName">The display name of the case whose failure is wanted.</param>
        /// <returns>The text the runner printed for that failure.</returns>
        private static string ExtractReportedFailure(string output, string caseName)
        {
            const string failedMarker = "failed ";

            int start = output.IndexOf(failedMarker + caseName, StringComparison.Ordinal);
            Assert.True(start >= 0, $"The run reported no failure for '{caseName}'.{Environment.NewLine}{output}");

            start += failedMarker.Length + caseName.Length;

            int next = output.IndexOf(Environment.NewLine + failedMarker, start, StringComparison.Ordinal);
            int summary = output.IndexOf("Test run summary", start, StringComparison.Ordinal);

            int end = next >= 0 ? next : output.Length;
            if (summary >= 0 && summary < end)
            {
                end = summary;
            }

            return output.Substring(start, end - start);
        }
    }
}
