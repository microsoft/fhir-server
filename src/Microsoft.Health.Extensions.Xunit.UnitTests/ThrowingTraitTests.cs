// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    /// <summary>
    /// Covers a discovery fault on a method that also carries a trait attribute that throws.
    /// </summary>
    /// <remarks>
    /// The traits of the lost method are read as one dictionary, and xUnit computes them on demand,
    /// so a single trait attribute that throws can take every other trait down with it. The failure
    /// reported in the method's place would then be missing the very traits a leg selects on, and a
    /// leg filtering positively would report success without it - the outcome reporting these
    /// failures exists to prevent.
    /// </remarks>
    public class ThrowingTraitTests
    {
        private const string FaultCase = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.ThrowingTrait.ThrowingTraitTests.NeverRuns (fixture argument set discovery: Sql)";
        private const string FaultCaseTwoDimensions = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.ThrowingTrait.ThrowingTraitTests.NeverRuns (fixture argument set discovery: Sql, Some)";

        private static Dictionary<string, string> BothFaultCases() =>
            new Dictionary<string, string>
            {
                [FaultCase] = "Failed",
                [FaultCaseTwoDimensions] = "Failed",
            };

        /// <summary>
        /// The failure standing in for the lost method is reported even though one of its trait
        /// attributes threw.
        /// </summary>
        [Fact]
        public void GivenAMethodWithATraitAttributeThatThrows_WhenItsDiscoveryFaults_ThenTheFailureIsStillReported()
        {
            TestAssetRun run = TestAssetRunner.Run("ThrowingTrait");

            TestAssetRunAssertions.PublishedExactly(
                run,
                BothFaultCases());
        }

        /// <summary>
        /// The trait the method's own declaration named still selects the failure. This is the
        /// shape of the export leg's filter, which requires a positive Category.
        /// </summary>
        [Fact]
        public void GivenAMethodWithATraitAttributeThatThrows_WhenALegSelectsOnAnOrdinaryTrait_ThenTheFailureIsStillSelected()
        {
            TestAssetRun run = TestAssetRunner.Run("ThrowingTrait", filterQueryTraits: "(Owner=ThrowingTraitOwner)");

            TestAssetRunAssertions.PublishedExactly(
                run,
                BothFaultCases());
        }

        /// <summary>
        /// The class-level trait selects it too, so the failure is visible to a leg filtering on the
        /// category the class declares.
        /// </summary>
        [Fact]
        public void GivenAMethodWithATraitAttributeThatThrows_WhenALegSelectsOnTheClassTrait_ThenTheFailureIsStillSelected()
        {
            TestAssetRun run = TestAssetRunner.Run("ThrowingTrait", filterQueryTraits: "(Category=ThrowingTraitProbe)");

            TestAssetRunAssertions.PublishedExactly(
                run,
                BothFaultCases());
        }
    }
}
