// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    /// <summary>
    /// The outcome of running one test asset scenario in a child process.
    /// </summary>
    public sealed class TestAssetRun
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TestAssetRun"/> class.
        /// </summary>
        /// <param name="exitCode">The exit code of the runner process.</param>
        /// <param name="output">The combined standard output and standard error of the runner.</param>
        /// <param name="results">The results the runner published to its TRX report.</param>
        /// <param name="errorCount">
        /// The number of runner-level errors reported, which are counted separately from failed
        /// tests and do not appear as results.
        /// </param>
        public TestAssetRun(int exitCode, string output, IReadOnlyList<TestAssetResult> results, int errorCount)
        {
            ExitCode = exitCode;
            Output = output;
            Results = results;
            ErrorCount = errorCount;
        }

        /// <summary>
        /// Gets the exit code of the runner process.
        /// </summary>
        public int ExitCode { get; }

        /// <summary>
        /// Gets the combined standard output and standard error of the runner, for diagnostics.
        /// </summary>
        public string Output { get; }

        /// <summary>
        /// Gets the results the runner published to its TRX report.
        /// </summary>
        public IReadOnlyList<TestAssetResult> Results { get; }

        /// <summary>
        /// Gets the number of runner-level errors reported. These are counted separately from
        /// failed tests and are not published as results, so a run can be wrong in ways that the
        /// result list alone does not show.
        /// </summary>
        public int ErrorCount { get; }

        /// <summary>
        /// Renders the run for use in assertion messages.
        /// </summary>
        /// <returns>The exit code, every result, and the runner output.</returns>
        public override string ToString()
        {
            var lines = Results.Select(r => $"  {r.Outcome} :: {r.Name ?? "<no name>"}");
            return $"exit code {ExitCode}, {Results.Count} result(s), {ErrorCount} runner error(s):{Environment.NewLine}"
                + string.Join(Environment.NewLine, lines)
                + $"{Environment.NewLine}--- runner output ---{Environment.NewLine}{Output}";
        }
    }
}
