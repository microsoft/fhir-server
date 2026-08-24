// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    /// <summary>
    /// Verifies discovery for a class that declares fixture argument sets on some methods but not on
    /// the class itself. Such a class cannot take the fast path that hands wholly undecorated classes
    /// straight to xUnit, yet it has no class-level sets to expand either, so its undecorated methods
    /// take a passthrough path of their own.
    /// </summary>
    public class PassthroughDiscoveryTests
    {
        private const string ClassName = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.MixedFixtureAttributes.MixedAttributeTests.";

        /// <summary>
        /// A method on the passthrough path is handed to xUnit with its arguments left unresolved, so
        /// that data attributes are still expanded downstream. Resolving them here instead would cost
        /// a theory every one of its rows, and losing rows shrinks the suite silently: the run stays
        /// green while the cases that would have failed simply never execute.
        /// </summary>
        [Fact]
        public void GivenAClassWithArgumentSetsOnOnlySomeMethods_WhenTheRunCompletes_ThenUndecoratedMethodsAreDiscoveredInFull()
        {
            TestAssetRun run = TestAssetRunner.Run("MixedFixtureAttributes");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [ClassName + "ExpandedMethod (Sql)"] = "Passed",
                    [ClassName + "ExpandedMethod (Cosmos)"] = "Passed",
                    [ClassName + "PassthroughFact"] = "Passed",
                    [ClassName + "PassthroughTheory(value: 1)"] = "Passed",
                    [ClassName + "PassthroughTheory(value: 2)"] = "Passed",
                    [ClassName + "PassthroughTheory(value: 3)"] = "Passed",
                });
        }

        /// <summary>
        /// The expanded method's variants have to carry the argument set trait even though the class
        /// declares no argument sets of its own, because the export and E2E legs select positively on
        /// that trait. A variant without it is not merely mislabelled: it is unreachable to those legs,
        /// which then run nothing of the sort and report green.
        /// </summary>
        /// <remarks>
        /// The methods on the passthrough path carry no argument set trait, which is correct - they
        /// were never expanded and there is no value to name - so this also pins that a leg naming one
        /// data store gets the one variant and nothing else.
        /// </remarks>
        [Fact]
        public void GivenAClassWithArgumentSetsOnOnlySomeMethods_WhenALegSelectsOneDataStore_ThenOnlyThatVariantIsSelected()
        {
            TestAssetRun run = TestAssetRunner.Run("MixedFixtureAttributes", filterTrait: "AssetDataStore=Sql");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [ClassName + "ExpandedMethod (Sql)"] = "Passed",
                });
        }
    }
}
