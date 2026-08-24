// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.AssemblyFixtureLifecycle
{
    /// <summary>
    /// Observes whether an assembly fixture that nothing asks for was still constructed.
    /// </summary>
    public class AssemblyFixtureLifecycleTests
    {
        /// <summary>
        /// The fixture is declared on the assembly and requested by no constructor anywhere, which is
        /// exactly how the real test assemblies use one.
        /// </summary>
        [Fact]
        public void GivenAnAssemblyFixtureNothingAsksFor_WhenATestRuns_ThenTheFixtureWasAlreadyConstructed()
        {
            Assert.True(
                AssemblyFixtureProbe.Constructed,
                "The assembly fixture had not been constructed by the time a test ran, so an assembly fixture used only for what its constructor does would have had no effect.");
        }
    }
}
