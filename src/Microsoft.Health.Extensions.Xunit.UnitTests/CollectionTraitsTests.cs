// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    /// <summary>
    /// Covers whether a collection's traits reach the tests of the classes in it.
    /// </summary>
    /// <remarks>
    /// Several CI legs in this repository select by excluding a category, so a trait arriving on a
    /// test from somewhere its own class never declared decides whether those legs run it. A class
    /// that joins a collection to be serialised against another one would, if collection traits
    /// propagate, also inherit that collection's categories and disappear from every leg excluding
    /// them - and an exclusion filter reports success for what it did not select, so nothing would
    /// say so. This pins the behaviour the test projects are written against.
    /// </remarks>
    public class CollectionTraitsTests
    {
        private const string ScenarioClass = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.CollectionTraits.JoinsTheCollectionTests";
        private const string OutsiderClass = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.CollectionTraits.StaysOutOfTheCollectionTests";

        /// <summary>
        /// Unfiltered, the test in the collection runs.
        /// </summary>
        [Fact]
        public void GivenAClassInATraitCarryingCollection_WhenItIsRunUnfiltered_ThenItIsReported()
        {
            TestAssetRun run = TestAssetRunner.Run("CollectionTraits");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [ScenarioClass + ".InheritsWhateverTheCollectionCarries"] = "Passed",
                    [OutsiderClass + ".CarriesNoCollectionTrait"] = "Passed",
                });
        }

        /// <summary>
        /// The shape that matters: a leg excluding the collection's category. xunit v3 puts a
        /// collection's traits on the tests of every class in it, so the member disappears even
        /// though its own class never declared that category, while the class outside the collection
        /// stays. Nothing in the leg's output would say the member was dropped, which is why any
        /// class joining a collection has to be read as carrying that collection's categories too.
        /// </summary>
        [Fact]
        public void GivenAClassInATraitCarryingCollection_WhenALegExcludesThatCategory_ThenOnlyTheCollectionsMemberIsDropped()
        {
            TestAssetRun run = TestAssetRunner.Run("CollectionTraits", filterNotTrait: "Category=CollectionOwned");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [OutsiderClass + ".CarriesNoCollectionTrait"] = "Passed",
                });
        }
    }
}
