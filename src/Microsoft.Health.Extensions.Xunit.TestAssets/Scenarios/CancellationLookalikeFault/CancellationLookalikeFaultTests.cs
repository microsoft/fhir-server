// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.FixtureVariants;
using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.CancellationLookalikeFault
{
    /// <summary>
    /// A class whose argument set attribute fails with an exception shaped like cancellation.
    /// </summary>
    /// <remarks>
    /// Nothing cancelled this run, so the failure belongs to the class and has to be reported as one.
    /// Reading it as a cancelled run instead would rethrow it and drop the class, and because a
    /// cancelled run is not a fault, the run would end green with these tests silently absent.
    /// </remarks>
    [CancellationLookalikeArgumentSets(AssetDataStore.Sql)]
    public class CancellationLookalikeFaultTests
    {
        /// <summary>
        /// Never runs: the class carries an argument set attribute that cannot be constructed.
        /// </summary>
        [Fact]
        public void NeverRuns()
        {
            Assert.True(true);
        }
    }
}
