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
        /// <returns>The exit code, output and published results of the run.</returns>
        public static TestAssetRun Run(string scenario, bool stopOnFail = false)
        {
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
                    "--filter-namespace",
                    ScenarioNamespacePrefix + scenario,
                    "--parallel",
                    "collections",
                    "--report-trx",
                    "--report-trx-filename",
                    trxFileName,
                    "--results-directory",
                    resultsDirectory,
                };

                if (stopOnFail)
                {
                    arguments.Add("--stop-on-fail");
                    arguments.Add("on");
                }

                (int exitCode, string output) = Execute(arguments);

                string trxPath = Path.Combine(resultsDirectory, trxFileName);
                if (!File.Exists(trxPath))
                {
                    throw new InvalidOperationException(
                        $"The '{scenario}' test asset run published no TRX report. Exit code {exitCode}.{Environment.NewLine}{output}");
                }

                return ParseTrx(trxPath, exitCode, output);
            }
            finally
            {
                try
                {
                    Directory.Delete(resultsDirectory, recursive: true);
                }
                catch (IOException)
                {
                    // A leaked temp directory is not worth failing an otherwise good test over.
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

            if (!File.Exists(path))
            {
                throw new InvalidOperationException(
                    $"The test assets assembly was not found at '{path}'. It is built by the project reference to Microsoft.Health.Extensions.Xunit.TestAssets.");
            }

            return path;
        }

        private static (int ExitCode, string Output) Execute(IReadOnlyList<string> arguments)
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
                    complete.Set();
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

            if (!process.WaitForExit((int)RunTimeout.TotalMilliseconds))
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException(
                    $"A test asset run did not finish within {RunTimeout}.{Environment.NewLine}{output}");
            }

            outputComplete.Wait(RunTimeout);
            errorComplete.Wait(RunTimeout);

            lock (output)
            {
                return (process.ExitCode, output.ToString());
            }
        }

        private static TestAssetRun ParseTrx(string trxPath, int exitCode, string output)
        {
            XNamespace trx = TrxNamespace;
            XDocument document = XDocument.Load(trxPath);

            List<TestAssetResult> results = document
                .Descendants(trx + "UnitTestResult")
                .Select(e => new TestAssetResult((string)e.Attribute("testName"), (string)e.Attribute("outcome")))
                .ToList();

            // Runner-level errors are counted here and nowhere else: they never become results, so
            // a run that mishandled its own bookkeeping still looks tidy in the result list.
            int errorCount = document
                .Descendants(trx + "Counters")
                .Select(e => (int?)e.Attribute("error") ?? 0)
                .FirstOrDefault();

            return new TestAssetRun(exitCode, output, results, errorCount);
        }
    }
}
