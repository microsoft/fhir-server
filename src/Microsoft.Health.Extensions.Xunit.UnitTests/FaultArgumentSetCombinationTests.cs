// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    /// <summary>
    /// Covers which failures a class whose expansion faulted is reported as, given the argument set
    /// attributes it declared.
    /// </summary>
    /// <remarks>
    /// The scenario assets exercise this end to end, but only over the one shape this repository
    /// happens to use: a single enum dimension whose values every method agrees on. The shapes that
    /// are not covered there are the ones where getting this wrong is invisible - a combination that
    /// should exist but does not is a CI leg passing green with the class's tests missing, which is
    /// precisely what reporting the fault as a test exists to prevent. They are pinned directly here
    /// rather than by adding assets no product code resembles.
    /// </remarks>
    public class FaultArgumentSetCombinationTests
    {
        [Flags]
        private enum Store
        {
            Sql = 1,
            Cosmos = 2,
        }

        [Flags]
        private enum Format
        {
            Json = 1,
            Ndjson = 2,
        }

        /// <summary>
        /// A class declaring nothing still has to be reported, or a fault in a class that takes no
        /// argument sets would produce no failing test at all.
        /// </summary>
        [Fact]
        public void GivenNoArgumentSets_WhenCombinationsAreBuilt_ThenASingleEmptyCombinationIsProduced()
        {
            var combinations = CustomXunitTestFrameworkDiscoverer.BuildFaultArgumentSetCombinations(Array.Empty<SingleFlag[][]>());

            Assert.Empty(Assert.Single(combinations));
        }

        /// <summary>
        /// Every variant an attribute would have expanded to needs its own failure, so that a leg
        /// selecting one variant by trait still sees a failure standing in for it.
        /// </summary>
        [Fact]
        public void GivenTwoDimensionsOnOneAttribute_WhenCombinationsAreBuilt_ThenTheirProductIsProduced()
        {
            var combinations = CustomXunitTestFrameworkDiscoverer.BuildFaultArgumentSetCombinations(
                new[] { new[] { Flags(Store.Sql, Store.Cosmos), Flags(Format.Json, Format.Ndjson) } });

            Assert.Equal(
                new[] { "Sql+Json", "Sql+Ndjson", "Cosmos+Json", "Cosmos+Ndjson" }.OrderBy(x => x, StringComparer.Ordinal),
                combinations.Select(Describe).OrderBy(x => x, StringComparer.Ordinal));
        }

        /// <summary>
        /// This is the case that makes each attribute's values its own product rather than one pooled
        /// product. Were the two pooled, the only combinations produced would carry a format, and a
        /// leg running <c>--filter-not-trait Format=Json</c> alongside one running
        /// <c>--filter-not-trait Format=Ndjson</c> would between them exclude every failure the class
        /// produced - both legs green, the whole class silently absent.
        /// </summary>
        [Fact]
        public void GivenTwoAttributesUsingDifferentDimensions_WhenCombinationsAreBuilt_ThenNeitherBorrowsTheOthersValues()
        {
            var combinations = CustomXunitTestFrameworkDiscoverer.BuildFaultArgumentSetCombinations(
                new[]
                {
                    new[] { Flags(Store.Sql, Store.Cosmos) },
                    new[] { Flags(Format.Json) },
                });

            Assert.Equal(
                new[] { "Cosmos", "Json", "Sql" },
                combinations.Select(Describe).OrderBy(x => x, StringComparer.Ordinal));
        }

        /// <summary>
        /// Methods usually declare the same argument sets as each other, and two failures standing for
        /// the same combination would collide on unique ID as well as reporting the fault twice.
        /// </summary>
        [Fact]
        public void GivenTwoAttributesDeclaringTheSameValues_WhenCombinationsAreBuilt_ThenTheCombinationIsProducedOnce()
        {
            var combinations = CustomXunitTestFrameworkDiscoverer.BuildFaultArgumentSetCombinations(
                new[]
                {
                    new[] { Flags(Store.Sql, Store.Cosmos) },
                    new[] { Flags(Store.Cosmos, Store.Sql) },
                });

            Assert.Equal(
                new[] { "Cosmos", "Sql" },
                combinations.Select(Describe).OrderBy(x => x, StringComparer.Ordinal));
        }

        /// <summary>
        /// An attribute whose values could not be expanded arrives here as no dimensions at all. It
        /// must not take the readable attributes' combinations down with it.
        /// </summary>
        [Fact]
        public void GivenAnAttributeWithNoUsableDimensions_WhenCombinationsAreBuilt_ThenTheOtherAttributesAreStillClosed()
        {
            var combinations = CustomXunitTestFrameworkDiscoverer.BuildFaultArgumentSetCombinations(
                new[]
                {
                    new[] { Array.Empty<SingleFlag>() },
                    new[] { Flags(Store.Sql, Store.Cosmos) },
                });

            Assert.Equal(
                new[] { string.Empty, "Cosmos", "Sql" },
                combinations.Select(Describe).OrderBy(x => x, StringComparer.Ordinal));
        }

        private static SingleFlag[] Flags(params Enum[] values)
        {
            return values.Select(value => new SingleFlag(value)).ToArray();
        }

        private static string Describe(IReadOnlyList<SingleFlag> combination)
        {
            return string.Join("+", combination.Select(flag => flag.EnumValue.ToString()));
        }
    }
}
