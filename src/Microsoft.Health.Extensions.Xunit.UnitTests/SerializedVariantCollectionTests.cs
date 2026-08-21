// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    /// <summary>
    /// Verifies that expanding a test class into fixture argument set variants leaves the class in
    /// the collection its author put it in.
    /// </summary>
    public class SerializedVariantCollectionTests
    {
        private const string FirstClass = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.SerializedVariants.FirstSerializedVariantTests";
        private const string SecondClass = "Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.SerializedVariants.SecondSerializedVariantTests";

        /// <summary>
        /// A collection is how an author says "these must not run at the same time". Giving each
        /// variant its own collection would let xUnit run them concurrently, and because the
        /// collection is named rather than backed by a <c>[CollectionDefinition]</c> class there is
        /// no definition to inspect and notice the grouping.
        /// </summary>
        [Fact]
        public void GivenClassesInOneNamedCollection_WhenTheirVariantsRun_ThenNoTwoRunConcurrently()
        {
            TestAssetRun run = TestAssetRunner.Run("SerializedVariants");

            TestAssetRunAssertions.PublishedExactly(
                run,
                new Dictionary<string, string>
                {
                    [FirstClass + ".RunsWithoutOverlappingTheOtherVariants (Sql)"] = "Passed",
                    [FirstClass + ".RunsWithoutOverlappingTheOtherVariants (Cosmos)"] = "Passed",
                    [SecondClass + ".RunsWithoutOverlappingTheOtherVariants (Sql)"] = "Passed",
                    [SecondClass + ".RunsWithoutOverlappingTheOtherVariants (Cosmos)"] = "Passed",
                    [SecondClass + ".ReceivesTheFixtureItsNameClaims (Sql)"] = "Passed",
                    [SecondClass + ".ReceivesTheFixtureItsNameClaims (Cosmos)"] = "Passed",
                });
        }
    }
}
