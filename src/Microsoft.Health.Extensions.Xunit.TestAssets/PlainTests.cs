// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets
{
    /// <summary>
    /// A test class with no fixture argument sets. Discovery for these classes reaches the shard filter through
    /// the base <c>XunitTestFrameworkDiscoverer</c> path rather than the fixture expansion path, so this class
    /// proves both paths are partitioned.
    /// </summary>
    /// <remarks>
    /// These methods are discovery fixtures, not real tests. Their bodies are intentionally empty.
    /// </remarks>
    public class PlainTests
    {
        [Fact]
        public void PlainFactOne()
        {
        }

        [Fact]
        public void PlainFactTwo()
        {
        }

        [Fact]
        public void PlainFactThree()
        {
        }

        [Fact]
        public void PlainFactFour()
        {
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void PlainTheory(int value)
        {
            _ = value;
        }
    }

    /// <summary>
    /// A second plain class, used to prove that sharding splits across classes as well as within one class.
    /// </summary>
    public class MorePlainTests
    {
        [Fact]
        public void MorePlainFactOne()
        {
        }

        [Fact]
        public void MorePlainFactTwo()
        {
        }

        [RetryFact]
        public void MorePlainRetryFact()
        {
        }

        [RetryTheory]
        [InlineData("a")]
        [InlineData("b")]
        public void MorePlainRetryTheory(string value)
        {
            _ = value;
        }
    }
}
