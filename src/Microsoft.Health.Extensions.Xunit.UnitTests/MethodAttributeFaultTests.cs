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
    /// Verifies that a method whose fixture argument set attribute cannot even be read costs only
    /// that method, rather than every method of its class.
    /// </summary>
    public class MethodAttributeFaultTests
    {
        private const string ScenarioClass = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.MethodAttributeFault.MethodAttributeFaultTests";
        private const string ErrorCaseName = ScenarioClass + ".NeverRuns (fixture argument set discovery: Cosmos)";

        /// <summary>
        /// Reading the attribute is a separate step from expanding it, and it can fail on its own: a
        /// method carrying two different fixture argument set attributes has no single one to expand.
        /// Reading every method's attribute up front, before the walk that isolates each method, put
        /// that failure outside the isolation and cost the whole class its tests - reported as one
        /// failure claiming the class never ran, with the healthy methods gone and no sign of it.
        /// Reading each method's attribute inside the walk keeps the loss to the method at fault.
        /// </summary>
        [Fact]
        public void GivenOneMethodWhoseAttributeCannotBeRead_WhenTheClassIsDiscovered_ThenTheOtherMethodsStillRun()
        {
            TestAssetRun run = TestAssetRunner.Run("MethodAttributeFault");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [ScenarioClass + ".RunsBeforeTheFault"] = "Passed",
                    [ScenarioClass + ".RunsAfterTheFault (Cosmos)"] = "Passed",
                    [ErrorCaseName] = "Failed",
                });

            Assert.NotEqual(0, run.ExitCode);
        }

        /// <summary>
        /// The values a fault case carries as traits are pooled from every method of the class, and
        /// reading them can hit the very declaration that caused the fault. Reading the whole class in
        /// one attempt meant that one unreadable method left the pool empty, so the failure went out
        /// with no argument set traits at all - and a leg selecting its tests by those traits would
        /// then skip the failure and pass with the class missing. Here only the failing method's
        /// values are unreadable, so the case must still carry the value its sibling declares.
        /// </summary>
        [Fact]
        public void GivenOneMethodWhoseAttributeCannotBeRead_WhenTestsAreSelectedByArgumentSetTrait_ThenTheFaultIsStillReported()
        {
            TestAssetRun run = TestAssetRunner.Run("MethodAttributeFault", filterTrait: "AssetDataStore=Cosmos");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [ScenarioClass + ".RunsAfterTheFault (Cosmos)"] = "Passed",
                    [ErrorCaseName] = "Failed",
                });

            Assert.NotEqual(0, run.ExitCode);
        }

        /// <summary>
        /// The failure has to name the method whose attribute could not be read, otherwise a class of
        /// many methods reports a fault with no indication of which declaration to go and fix.
        /// </summary>
        [Fact]
        public void GivenOneMethodWhoseAttributeCannotBeRead_WhenTheClassIsDiscovered_ThenTheFailureNamesOnlyThatMethod()
        {
            TestAssetRun run = TestAssetRunner.Run("MethodAttributeFault");

            Assert.Contains("MethodAttributeFaultTests.NeverRuns", run.Output, StringComparison.Ordinal);
            Assert.Contains("Other methods of the class were discovered normally", run.Output, StringComparison.Ordinal);
        }
    }
}
