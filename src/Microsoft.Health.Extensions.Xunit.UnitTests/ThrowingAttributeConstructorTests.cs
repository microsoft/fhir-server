// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    /// <summary>
    /// Covers a discovery fault on a class that also carries an attribute whose constructor throws.
    /// </summary>
    /// <remarks>
    /// This is a wider hole than a trait attribute that refuses to produce its traits. A
    /// declaration's attributes are constructed together, so one throwing constructor fails the read
    /// for the whole declaration - and the attribute that throws need not have anything to do with
    /// traits, which is what makes it easy to arrive at without meaning to. The class trait would go
    /// with it, and a leg selecting positively on that trait would report success with the class's
    /// tests missing.
    /// </remarks>
    public class ThrowingAttributeConstructorTests
    {
        private const string FaultCase = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.ThrowingAttributeConstructor.ThrowingAttributeConstructorTests.NeverRuns (fixture argument set discovery: Sql)";
        private const string FaultCaseTwoDimensions = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.ThrowingAttributeConstructor.ThrowingAttributeConstructorTests.NeverRuns (fixture argument set discovery: Sql, Some)";

        private static Dictionary<string, string> BothFaultCases() =>
            new Dictionary<string, string>
            {
                [FaultCase] = "Failed",
                [FaultCaseTwoDimensions] = "Failed",
            };

        /// <summary>
        /// The failure standing in for the lost tests is reported even though constructing the
        /// class's attributes throws.
        /// </summary>
        [Fact]
        public void GivenAClassWithAnAttributeConstructorThatThrows_WhenItsDiscoveryFaults_ThenTheFailureIsStillReported()
        {
            TestAssetRun run = TestAssetRunner.Run("ThrowingAttributeConstructor");

            TestAssetRunAssertions.PublishedExactly(
                run,
                BothFaultCases());
        }

        /// <summary>
        /// The class trait still selects the failure, even though the attribute that throws sits on
        /// the same class. This is the shape of the export leg's filter, which requires a positive
        /// Category, and the reason the sound attributes must survive the broken one.
        /// </summary>
        [Fact]
        public void GivenAClassWithAnAttributeConstructorThatThrows_WhenALegSelectsOnTheClassTrait_ThenTheFailureIsStillSelected()
        {
            TestAssetRun run = TestAssetRunner.Run("ThrowingAttributeConstructor", filterQueryTraits: "(Category=ThrowingConstructorProbe)");

            TestAssetRunAssertions.PublishedExactly(
                run,
                BothFaultCases());
        }

        /// <summary>
        /// The argument set trait selects it too. That trait is built from the argument sets rather
        /// than read from the declaration, so this pins that the failed read costs the failure
        /// nothing beyond the declaration it happened on.
        /// </summary>
        [Fact]
        public void GivenAClassWithAnAttributeConstructorThatThrows_WhenALegSelectsOnTheArgumentSet_ThenTheFailureIsStillSelected()
        {
            TestAssetRun run = TestAssetRunner.Run("ThrowingAttributeConstructor", filterQueryTraits: "(AssetDataStore=Sql)");

            TestAssetRunAssertions.PublishedExactly(
                run,
                BothFaultCases());
        }
    }
}
