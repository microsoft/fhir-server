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
    /// Verifies that a fixture argument set which names no value fails the run rather than quietly
    /// removing the tests that asked for it.
    /// </summary>
    public class EmptyArgumentSetTests
    {
        private const string ErrorCaseName = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.EmptyArgumentSet.EmptyArgumentSetTests.NeverRuns (fixture argument set discovery)";

        /// <summary>
        /// An argument set naming no flag collapses the product of the declared dimensions to nothing,
        /// so the method produces no test cases. Left alone that is indistinguishable from a class with
        /// no tests: the run reports success with the tests simply absent, which is the worst way for a
        /// test to stop running because nothing in the report says it ever existed. A declared argument
        /// set that expands to nothing is always a misconfiguration, so it is reported as a failure.
        /// </summary>
        [Fact]
        public void GivenAnArgumentSetThatNamesNoValue_WhenTheClassIsDiscovered_ThenTheRunFails()
        {
            TestAssetRun run = TestAssetRunner.Run("EmptyArgumentSet");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [ErrorCaseName] = "Failed",
                });

            Assert.NotEqual(0, run.ExitCode);
        }

        /// <summary>
        /// The class takes no fixture, so nothing is aggregated ahead of the reported case and the
        /// message it carries is the discoverer's own. That is what makes this failure actionable:
        /// it names the method and says what is wrong with the argument set it declared.
        /// </summary>
        [Fact]
        public void GivenAnArgumentSetThatNamesNoValue_WhenTheClassIsDiscovered_ThenTheFailureNamesTheMethod()
        {
            TestAssetRun run = TestAssetRunner.Run("EmptyArgumentSet");

            Assert.Contains("EmptyArgumentSetTests.NeverRuns", run.Output, StringComparison.Ordinal);
            Assert.Contains("expanded to no fixture argument sets", run.Output, StringComparison.Ordinal);
        }
    }
}
