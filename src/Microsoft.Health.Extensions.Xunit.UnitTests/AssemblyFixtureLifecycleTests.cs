// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    /// <summary>
    /// Verifies that the custom framework still creates an assembly fixture that no test asks for.
    /// </summary>
    /// <remarks>
    /// This repository has nine test assemblies whose only use of an assembly fixture is the side
    /// effect of its constructor - installing the FHIR model info provider - and none of them names
    /// the fixture as a constructor argument anywhere. If unrequested fixtures were created lazily,
    /// or not at all, the provider would never be installed and the failure would land somewhere far
    /// from the cause. Nothing else in the repository states that expectation, so it is stated here.
    /// </remarks>
    public class AssemblyFixtureLifecycleTests
    {
        /// <summary>
        /// The fixture is declared on the assets assembly and requested by nothing, so a test seeing
        /// it already constructed is the whole of what the nine assemblies depend on.
        /// </summary>
        [Fact]
        public void GivenAnAssemblyFixtureNoTestAsksFor_WhenTheRunReachesATest_ThenTheFixtureHasBeenConstructed()
        {
            TestAssetRun run = TestAssetRunner.Run("AssemblyFixtureLifecycle");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    ["Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.AssemblyFixtureLifecycle.AssemblyFixtureLifecycleTests.GivenAnAssemblyFixtureNothingAsksFor_WhenATestRuns_ThenTheFixtureWasAlreadyConstructed"] = "Passed",
                });
        }
    }
}
