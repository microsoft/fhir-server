// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Xunit;

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    public class OrdinaryTests
    {
        [Fact]
        public void GivenNoFixtureVariants_TheTestRuns()
        {
            Assert.True(true);
        }
    }
}
