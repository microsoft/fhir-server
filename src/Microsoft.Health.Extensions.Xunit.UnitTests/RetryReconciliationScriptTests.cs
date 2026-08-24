// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    /// <summary>
    /// Covers the script the CI legs use to decide whether a retried failure really cleared.
    /// </summary>
    /// <remarks>
    /// Every test leg runs with retries and lets the runner's exit code be the verdict, and the
    /// runner exits zero when a failing test stops failing without passing. The script in
    /// <c>build/jobs/scripts/Assert-RetriedFailuresPassed.ps1</c> is the only thing that turns that
    /// back into a red leg, so a mistake in it puts a real failure back on the green path it was
    /// written to close. These tests run the real script over reports shaped like the ones the
    /// runner writes.
    /// </remarks>
    public class RetryReconciliationScriptTests
    {
        private const string TrxNamespace = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        private const string DefaultStorage = @"c:\bin\assembly.dll";

        private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Two tests whose names differ only in case, where the failing one never passed.
        /// </summary>
        /// <remarks>
        /// Display names are not identities. A theory whose rows differ only in the case of an
        /// argument produces exactly this shape, and the repository already has one:
        /// <c>VersioningConfigurationTests</c> passes both <c>"Versioned"</c> and the constant whose
        /// value is <c>"versioned"</c> to the same theory. Keyed by name in a comparison that
        /// ignores case, the row that passed answers for the row that did not, and the failure the
        /// script exists to catch leaves through the door it was meant to close.
        /// </remarks>
        [Fact]
        public void GivenTwoTestsNamedAlikeApartFromCase_WhenOnlyOneOfThemPassed_ThenTheOtherIsStillReportedAsUnresolved()
        {
            var results = new ResultsDirectory();

            results.WriteAttempt(
                Result("11111111-1111-1111-1111-111111111111", "T.Row(input: \"Versioned\")", "Failed"));

            results.WriteFinal(
                Result("11111111-1111-1111-1111-111111111111", "T.Row(input: \"Versioned\")", "NotExecuted"),
                Result("22222222-2222-2222-2222-222222222222", "T.Row(input: \"versioned\")", "Passed"));

            ScriptRun run = results.Reconcile();

            Assert.Equal(1, run.ExitCode);
            Assert.Contains("T.Row(input: \"Versioned\") -> NotExecuted", run.Output, StringComparison.Ordinal);
        }

        /// <summary>
        /// Two tests sharing one display name exactly, both of which passed in the end.
        /// </summary>
        /// <remarks>
        /// This is the other half of telling tests apart by identity: distinguishing them must not
        /// come at the cost of reporting a test that did pass, or the legs learn to ignore this
        /// script.
        /// </remarks>
        [Fact]
        public void GivenTwoTestsSharingADisplayName_WhenBothOfThemPassed_ThenNothingIsReported()
        {
            var results = new ResultsDirectory();

            results.WriteAttempt(
                Result("11111111-1111-1111-1111-111111111111", "T.Row", "Failed"),
                Result("22222222-2222-2222-2222-222222222222", "T.Row", "Failed"));

            results.WriteFinal(
                Result("11111111-1111-1111-1111-111111111111", "T.Row", "Passed"),
                Result("22222222-2222-2222-2222-222222222222", "T.Row", "Passed"));

            ScriptRun run = results.Reconcile();

            Assert.Equal(0, run.ExitCode);
            Assert.Contains("(2 reconciled)", run.Output, StringComparison.Ordinal);
        }

        /// <summary>
        /// A test that failed and then passed, which is what a retry is for.
        /// </summary>
        [Fact]
        public void GivenAFailureThatPassedOnTheNextAttempt_WhenReconciling_ThenTheLegIsLeftGreen()
        {
            var results = new ResultsDirectory();

            results.WriteAttempt(Result("11111111-1111-1111-1111-111111111111", "T.Flaky", "Failed"));
            results.WriteFinal(Result("11111111-1111-1111-1111-111111111111", "T.Flaky", "Passed"));

            ScriptRun run = results.Reconcile();

            Assert.Equal(0, run.ExitCode);
            Assert.Contains("(1 reconciled)", run.Output, StringComparison.Ordinal);
        }

        /// <summary>
        /// A test that failed and then skipped, which the runner reports as a success.
        /// </summary>
        [Fact]
        public void GivenAFailureThatSkippedOnTheNextAttempt_WhenReconciling_ThenTheLegIsFailed()
        {
            var results = new ResultsDirectory();

            results.WriteAttempt(Result("11111111-1111-1111-1111-111111111111", "T.Flaky", "Failed"));
            results.WriteFinal(Result("11111111-1111-1111-1111-111111111111", "T.Flaky", "NotExecuted"));

            ScriptRun run = results.Reconcile();

            Assert.Equal(1, run.ExitCode);
            Assert.Contains("T.Flaky -> NotExecuted", run.Output, StringComparison.Ordinal);
        }

        /// <summary>
        /// A test that failed and then was not recorded in the final attempt at all.
        /// </summary>
        [Fact]
        public void GivenAFailureAbsentFromTheFinalAttempt_WhenReconciling_ThenTheLegIsFailed()
        {
            var results = new ResultsDirectory();

            results.WriteAttempt(Result("11111111-1111-1111-1111-111111111111", "T.Vanished", "Failed"));
            results.WriteFinal(Result("22222222-2222-2222-2222-222222222222", "T.Other", "Passed"));

            ScriptRun run = results.Reconcile();

            Assert.Equal(1, run.ExitCode);
            Assert.Contains("T.Vanished -> not run at all", run.Output, StringComparison.Ordinal);
        }

        /// <summary>
        /// The same test identity reported by two different assemblies.
        /// </summary>
        /// <remarks>
        /// This repository compiles the same shared test files into several assemblies, so the same
        /// fully qualified name is a real test in each of them and the reports can give them the
        /// same identifier. A pass in one assembly must not answer for a failure in another.
        /// </remarks>
        [Fact]
        public void GivenOneTestIdentityInTwoAssemblies_WhenOnlyOneOfThemPassed_ThenTheOtherIsStillReportedAsUnresolved()
        {
            var results = new ResultsDirectory();

            results.WriteAttempt(
                Result("11111111-1111-1111-1111-111111111111", "Shared.T.Case", "Failed", storage: @"c:\bin\r4.dll"));

            // Each assembly reports its own results, so the two reports below are what the same test
            // identity coming from two assemblies actually looks like on disk.
            results.WriteFinal(
                Result("11111111-1111-1111-1111-111111111111", "Shared.T.Case", "NotExecuted", storage: @"c:\bin\r4.dll"));

            results.WriteFinal(
                Result("11111111-1111-1111-1111-111111111111", "Shared.T.Case", "Passed", storage: @"c:\bin\r5.dll"));

            ScriptRun run = results.Reconcile();

            Assert.Equal(1, run.ExitCode);
            Assert.Contains("Shared.T.Case -> NotExecuted", run.Output, StringComparison.Ordinal);
        }

        /// <summary>
        /// A result the report gives no identifier, which nothing can be matched against.
        /// </summary>
        [Fact]
        public void GivenAResultWithNoTestId_WhenReconciling_ThenTheLegIsFailedWithAnExplanation()
        {
            var results = new ResultsDirectory();

            results.WriteAttempt(Result(testId: null, "T.Anonymous", "Failed"));
            results.WriteFinal(Result("11111111-1111-1111-1111-111111111111", "T.Anonymous", "Passed"));

            ScriptRun run = results.Reconcile();

            Assert.Equal(1, run.ExitCode);
            Assert.Contains("with no test id", run.Output, StringComparison.Ordinal);
        }

        /// <summary>
        /// Reports that recorded nothing at all, which is what an empty run writes.
        /// </summary>
        /// <remarks>
        /// The verdict for a leg that ran nothing belongs to the discovery floor, which says so in
        /// as many words. This script only has to leave that message intact instead of replacing it
        /// with an error about reading XML.
        /// </remarks>
        [Fact]
        public void GivenReportsThatRecordedNothing_WhenReconciling_ThenTheLegIsLeftGreenWithoutAnError()
        {
            var results = new ResultsDirectory();

            results.WriteAttempt();
            results.WriteFinal();

            ScriptRun run = results.Reconcile();

            Assert.Equal(0, run.ExitCode);
            Assert.Contains("nothing to reconcile", run.Output, StringComparison.Ordinal);
        }

        /// <summary>
        /// A run with no retries below it, which is every run where nothing failed.
        /// </summary>
        [Fact]
        public void GivenNoRetryAttempts_WhenReconciling_ThenTheLegIsLeftGreen()
        {
            var results = new ResultsDirectory();

            results.WriteFinal(Result("11111111-1111-1111-1111-111111111111", "T.Steady", "Passed"));

            ScriptRun run = results.Reconcile();

            Assert.Equal(0, run.ExitCode);
            Assert.Contains("nothing to reconcile", run.Output, StringComparison.Ordinal);
        }

        /// <summary>
        /// Retry attempts with no final report beside them, so nothing says how they ended.
        /// </summary>
        [Fact]
        public void GivenRetryAttemptsWithNoFinalReport_WhenReconciling_ThenTheLegIsFailed()
        {
            var results = new ResultsDirectory();

            results.WriteAttempt(Result("11111111-1111-1111-1111-111111111111", "T.Flaky", "Failed"));

            ScriptRun run = results.Reconcile();

            Assert.Equal(1, run.ExitCode);
            Assert.Contains("no final report", run.Output, StringComparison.Ordinal);
        }

        private static TestResult Result(string testId, string testName, string outcome, string storage = DefaultStorage)
            => new TestResult(testId, testName, outcome, storage);

        private sealed record TestResult(string TestId, string TestName, string Outcome, string Storage);

        /// <summary>
        /// A results directory shaped the way the retry extension leaves one behind.
        /// </summary>
        private sealed class ResultsDirectory
        {
            private readonly string _root = Path.Combine(Path.GetTempPath(), "xunit-ext-reconcile", Guid.NewGuid().ToString("N"));
            private int _attempts;
            private int _reports;

            public void WriteAttempt(params TestResult[] results)
            {
                _attempts++;
                Write(Path.Combine(_root, "Retries", "run", _attempts.ToString(CultureInfo.InvariantCulture), "report.trx"), results);
            }

            /// <summary>
            /// Writes one final report. Each call writes a separate report, which is how results
            /// from more than one assembly reach the same results directory.
            /// </summary>
            public void WriteFinal(params TestResult[] results)
            {
                _reports++;
                Write(Path.Combine(_root, FormattableString.Invariant($"report{_reports}.trx")), results);
            }

            public ScriptRun Reconcile()
            {
                try
                {
                    return ScriptRunner.Run(
                        ScriptRunner.Resolve("RetryReconciliationScript"),
                        new Dictionary<string, string> { ["ResultsDirectory"] = _root });
                }
                finally
                {
                    try
                    {
                        Directory.Delete(_root, recursive: true);
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        // A leaked temp directory is not worth failing an otherwise good test over.
                    }
                }
            }

            private static void Write(string path, IReadOnlyList<TestResult> results)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));

                XNamespace ns = TrxNamespace;

                var document = new XDocument(
                    new XElement(
                        ns + "TestRun",
                        new XAttribute("id", Guid.NewGuid().ToString()),
                        new XElement(
                            ns + "Results",
                            results.Select(r => new XElement(
                                ns + "UnitTestResult",
                                r.TestId == null ? null : new XAttribute("testId", r.TestId),
                                new XAttribute("testName", r.TestName),
                                new XAttribute("outcome", r.Outcome)))),
                        new XElement(
                            ns + "TestDefinitions",
                            results.Where(r => r.TestId != null).Select(r => new XElement(
                                ns + "UnitTest",
                                new XAttribute("id", r.TestId),
                                new XAttribute("name", r.TestName),
                                new XAttribute("storage", r.Storage))))));

                document.Save(path);
            }
        }
    }
}
