// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets.Scenarios.CollectionTraits
{
    /// <summary>
    /// Stays out of the collection, so a filter that removes the collection's members can be told
    /// apart from one that selected nothing at all.
    /// </summary>
    public class StaysOutOfTheCollectionTests
    {
        /// <summary>
        /// Selected by any filter that does not name a trait this test carries.
        /// </summary>
        [Fact]
        public void CarriesNoCollectionTrait()
        {
            Assert.True(true);
        }
    }
}
