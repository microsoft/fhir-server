// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Xml.Linq;

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    /// <summary>
    /// Runs one scenario from the test assets assembly in a child process and reads back the
    /// results it published.
    /// </summary>
    /// <remarks>
    /// The behaviours under test -- how many results a run publishes, what they are named, and
    /// whether a result is published at all -- are properties of a whole xUnit run, so they cannot
    /// be observed from inside the run that is asserting on them.
    /// </remarks>
    public static class TestAssetRunner
    {
        private const string TrxNamespace = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
        private const string ScenarioNamespacePrefix = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.";

        private static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Runs a single scenario and returns everything it reported.
        /// </summary>
        /// <param name="scenario">
        /// The scenario folder name, which is also the last segment of the scenario's namespace.
        /// </param>
        /// <param name="stopOnFail">
        /// Whether to cancel the run as soon as a test fails. Scenarios that exercise cancellation
        /// rely on this to cancel the run from inside it.
        /// </param>
        /// <param name="preEnumerateTheories">
        /// Whether the runner may resolve theory data at discovery time. Turning this off is the
        /// supported way to reach the delay-enumerated code path, which a theory also reaches on
        /// its own when its data cannot be serialized.
        /// </param>
        /// <param name="filterTrait">
        /// A <c>Name=Value</c> trait filter to apply on top of the namespace filter, in the form the
        /// runner takes on the command line. CI legs select their tests this way, so this is how a
        /// scenario checks that it is still selected when they do.
        /// </param>
        /// <param name="maxThreads">
        /// The value to pass as the runner's thread limit, in the form it takes on the command line.
        /// Scenarios that observe how much runs at once set this so the expected bound is explicit.
        /// </param>
        /// <param name="filterNotTrait">
        /// A <c>Name=Value</c> trait filter to exclude on, in the form the runner takes on the command
        /// line. The repository's integration legs select their tests this way - each excludes the
        /// other data store - so this is how a scenario checks that a leg excluding one value still
        /// sees what it was meant to run.
        /// </param>
        /// <param name="filterQueryTraits">
        /// The trait predicate of a query filter, in the form the runner takes inside the brackets of a
        /// query - such as <c>(DataStore=CosmosDb)&amp;(Category=ExportLongRunning)</c>. This is the
        /// compound form the repository's E2E and export legs select with, where a case runs only if it
        /// carries every named trait, so a failure missing any one of them is invisible to the leg that
        /// would have run the tests it stands for. The runner scopes the query to the scenario itself,
        /// because a query filter cannot be combined with the plain namespace filter used otherwise.
        /// </param>
        /// <returns>The exit code, output and published results of the run.</returns>
        public static TestAssetRun Run(string scenario, bool stopOnFail = false, bool preEnumerateTheories = true, string filterTrait = null, string maxThreads = null, string filterNotTrait = null, string filterQueryTraits = null)
        {
            // The runner rejects a query filter outright once any plain filter has been added, so the
            // two forms cannot be mixed. Saying that here keeps a scenario that tries from failing as an
            // unhandled exception inside the asset, which reads as the asset being broken.
            if (!string.IsNullOrEmpty(filterQueryTraits) && (!string.IsNullOrEmpty(filterTrait) || !string.IsNullOrEmpty(filterNotTrait)))
            {
                throw new ArgumentException("A query filter cannot be combined with a trait filter; express the whole selection as a query.", nameof(filterQueryTraits));
            }

            string assetsAssembly = ResolveAssetsAssembly();
            string resultsDirectory = Path.Combine(Path.GetTempPath(), "xunit-ext-assets", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(resultsDirectory);

            try
            {
                const string trxFileName = "scenario.trx";

                // The assets are launched through Microsoft Testing Platform because that is the
                // runner CI uses: reporters differ in how they handle a result published for a
                // test they have already finished, so asserting against a different runner would
                // not say anything about the behaviour that actually ships.
                var arguments = new List<string>
                {
                    "exec",
                    assetsAssembly,
                    "--parallel",
                    "collections",
                    "--report-trx",
                    "--report-trx-filename",
                    trxFileName,
                    "--results-directory",
                    resultsDirectory,
                };

                if (string.IsNullOrEmpty(filterQueryTraits))
                {
                    arguments.Add("--filter-namespace");
                    arguments.Add(ScenarioNamespacePrefix + scenario);
                }
                else
                {
                    // A query names each level of the test's identity in turn - assembly, namespace,
                    // class, method - before the traits, so scoping it to the scenario means matching
                    // any assembly and any class and method within that one namespace.
                    arguments.Add("--filter-query");
                    arguments.Add(FormattableString.Invariant($"/*/{ScenarioNamespacePrefix}{scenario}/*/*/[{filterQueryTraits}]"));
                }

                if (stopOnFail)
                {
                    arguments.Add("--stop-on-fail");
                    arguments.Add("on");
                }

                if (!preEnumerateTheories)
                {
                    arguments.Add("--pre-enumerate-theories");
                    arguments.Add("off");
                }

                if (!string.IsNullOrEmpty(filterTrait))
                {
                    arguments.Add("--filter-trait");
                    arguments.Add(filterTrait);
                }

                if (!string.IsNullOrEmpty(filterNotTrait))
                {
                    arguments.Add("--filter-not-trait");
                    arguments.Add(filterNotTrait);
                }

                if (!string.IsNullOrEmpty(maxThreads))
                {
                    arguments.Add("--max-threads");
                    arguments.Add(maxThreads);
                }

                (int exitCode, string output, TimeSpan duration) = Execute(arguments);

                string trxPath = Path.Combine(resultsDirectory, trxFileName);
                if (!File.Exists(trxPath))
                {
                    throw new InvalidOperationException(
                        $"The '{scenario}' test asset run published no TRX report. Exit code {exitCode}.{Environment.NewLine}{output}");
                }

                return ParseTrx(trxPath, exitCode, output, duration);
            }
            finally
            {
                try
                {
                    Directory.Delete(resultsDirectory, recursive: true);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // A leaked temp directory is not worth failing an otherwise good test over.
                    // Windows in particular reports a file still held open as an access violation
                    // rather than an IO error, so both have to be tolerated here.
                }
            }
        }

        private static string ResolveAssetsAssembly()
        {
            string path = typeof(TestAssetRunner).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "TestAssetsAssembly")
                ?.Value;

            if (string.IsNullOrEmpty(path))
            {
                throw new InvalidOperationException(
                    "The TestAssetsAssembly metadata attribute is missing. It is injected by the project file and identifies the assembly holding the scenarios.");
            }

            // The project file writes this path with the separator MSBuild used on the machine that
            // built it, and a backslash is an ordinary filename character everywhere except Windows.
            path = Path.GetFullPath(path.Replace('\\', Path.DirectorySeparatorChar));

            if (!File.Exists(path))
            {
                throw new InvalidOperationException(
                    $"The test assets assembly was not found at '{path}'. It is built by the project reference to Microsoft.Health.Extensions.Xunit.TestAssets.");
            }

            return path;
        }

        private static (int ExitCode, string Output, TimeSpan Duration) Execute(IReadOnlyList<string> arguments)
        {
            var startInfo = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            // The pipeline sets this so its own test runs land in a known place. The child would
            // inherit it and could write its TRX somewhere other than --results-directory, which
            // would look exactly like a run that produced no report at all.
            startInfo.Environment.Remove("platformOptions__resultDirectory");

            var output = new StringBuilder();
            using var outputComplete = new ManualResetEventSlim(false);
            using var errorComplete = new ManualResetEventSlim(false);
            using var process = new Process { StartInfo = startInfo };

            void Append(DataReceivedEventArgs e, ManualResetEventSlim complete)
            {
                if (e.Data == null)
                {
                    try
                    {
                        complete.Set();
                    }
                    catch (ObjectDisposedException)
                    {
                        // The only way this event is disposed while a callback is still in flight is
                        // that Execute has already abandoned the wait and is unwinding on the timeout
                        // path. Nothing is listening for the signal any more, and letting it escape
                        // would take down the whole test host from a thread pool thread, replacing the
                        // timeout report with an unrelated crash.
                    }
                }
                else
                {
                    lock (output)
                    {
                        output.AppendLine(e.Data);
                    }
                }
            }

            process.OutputDataReceived += (_, e) => Append(e, outputComplete);
            process.ErrorDataReceived += (_, e) => Append(e, errorComplete);

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            long startedAt = Stopwatch.GetTimestamp();

            if (!process.WaitForExit((int)RunTimeout.TotalMilliseconds))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // The child can exit on its own between the wait giving up and the kill landing.
                    // Letting that race escape would replace the timeout report -- which carries the
                    // child's output -- with an unrelated and far less useful exception.
                }

                // The child is still writing on its background threads, so the buffer has to be
                // snapshotted under the same lock its callbacks take. Reading it directly here
                // would race those appends and surface as an obscure StringBuilder crash instead
                // of the timeout that actually happened.
                string captured;
                lock (output)
                {
                    captured = output.ToString();
                }

                throw new TimeoutException(
                    $"A test asset run did not finish within {RunTimeout}.{Environment.NewLine}{captured}");
            }

            TimeSpan duration = Stopwatch.GetElapsedTime(startedAt);

            // Exit does not imply the redirected streams have been drained. Waiting for the EOF
            // signal on each is what makes the captured output complete; if that wait itself times
            // out the output is truncated, which would otherwise quietly weaken every diagnostic
            // built from it.
            bool drained = outputComplete.Wait(RunTimeout) & errorComplete.Wait(RunTimeout);

            lock (output)
            {
                if (!drained)
                {
                    output.AppendLine("[TestAssetRunner] The child process output was not fully drained before this run was read, so the text above may be truncated.");
                }

                return (process.ExitCode, output.ToString(), duration);
            }
        }

        private static TestAssetRun ParseTrx(string trxPath, int exitCode, string output, TimeSpan duration)
        {
            XNamespace trx = TrxNamespace;
            XDocument document = XDocument.Load(trxPath);

            List<TestAssetResult> results = document
                .Descendants(trx + "UnitTestResult")
                .Select(e => new TestAssetResult((string)e.Attribute("testName"), (string)e.Attribute("outcome")))
                .ToList();

            // Runner-level errors are counted here and nowhere else: they never become results, so
            // a run that mishandled its own bookkeeping still looks tidy in the result list.
            //
            // A missing counter is not the same as a count of zero. Defaulting it to zero would
            // turn the guard built on it into a no-op the moment the report shape changed, and the
            // guard would keep passing while checking nothing.
            XAttribute errorAttribute = document
                .Descendants(trx + "Counters")
                .Attributes("error")
                .FirstOrDefault();

            if (errorAttribute == null)
            {
                throw new InvalidOperationException(
                    $"The TRX report at '{trxPath}' has no Counters/@error attribute, so runner-level errors cannot be read from it.");
            }

            return new TestAssetRun(exitCode, output, results, (int)errorAttribute, duration);
        }
    }
}
