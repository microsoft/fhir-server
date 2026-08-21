// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    /// <summary>
    /// Assertions over what a test asset run published.
    /// </summary>
    public static class TestAssetRunAssertions
    {
        private const int SuccessExitCode = 0;
        private const int FailedTestsExitCode = 2;

        /// <summary>
        /// Asserts that a run published exactly the expected results and nothing else.
        /// </summary>
        /// <remarks>
        /// Comparing the whole result set, rather than counting outcomes, is what catches the
        /// failure modes these tests exist for: a result that was never published, a result
        /// published twice, and a result published without a display name because it was
        /// attributed to a test the runner had already finished.
        /// </remarks>
        /// <param name="run">The run to assert on.</param>
        /// <param name="expected">
        /// The expected results, as display name to outcome. Order is not significant.
        /// </param>
        public static void PublishedExactly(TestAssetRun run, IReadOnlyDictionary<string, string> expected)
        {
            EnsureNoRunnerErrors(run);
            EnsureNoOrphanedResults(run);
            EnsureExitCodeMatchesResults(run, expected);

            string[] actualEntries = run.Results
                .Select(r => $"{r.Outcome} :: {r.Name}")
                .OrderBy(e => e, StringComparer.Ordinal)
                .ToArray();

            string[] expectedEntries = expected
                .Select(kvp => $"{kvp.Value} :: {kvp.Key}")
                .OrderBy(e => e, StringComparer.Ordinal)
                .ToArray();

            string message = $"The run did not publish the expected results.{Environment.NewLine}"
                + $"Expected:{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", expectedEntries)}{Environment.NewLine}"
                + $"Actual: {run}";

            Assert.True(actualEntries.SequenceEqual(expectedEntries, StringComparer.Ordinal), message);
        }

        private static void EnsureNoRunnerErrors(TestAssetRun run)
        {
            string message = $"The run reported {run.ErrorCount} runner-level error(s). These are counted separately "
                + $"from failed tests and are never published as results, so the result list below can look correct "
                + $"even though the run went wrong.{Environment.NewLine}Actual: {run}";

            Assert.True(run.ErrorCount == 0, message);
        }

        private static void EnsureNoOrphanedResults(TestAssetRun run)
        {
            int orphaned = run.Results.Count(r => string.IsNullOrWhiteSpace(r.Name));

            string message = $"The run published {orphaned} result(s) with no display name, which means a result was "
                + $"attributed to a test the runner had already finished.{Environment.NewLine}Actual: {run}";

            Assert.True(orphaned == 0, message);
        }

        private static void EnsureExitCodeMatchesResults(TestAssetRun run, IReadOnlyDictionary<string, string> expected)
        {
            // The published results and the exit code are reported through different paths, so a
            // child that writes a correct-looking report and then dies -- in a fixture teardown,
            // say -- would otherwise be indistinguishable from a clean run.
            bool expectingFailures = expected.Values.Any(outcome => !string.Equals(outcome, "Passed", StringComparison.Ordinal));
            int expectedExitCode = expectingFailures ? FailedTestsExitCode : SuccessExitCode;

            string message = $"The run exited with code {run.ExitCode}, but {expectedExitCode} was expected for these "
                + $"results. An unexpected code means the run ended for a reason its published results do not "
                + $"show.{Environment.NewLine}Actual: {run}";

            Assert.True(run.ExitCode == expectedExitCode, message);
        }
    }
}
