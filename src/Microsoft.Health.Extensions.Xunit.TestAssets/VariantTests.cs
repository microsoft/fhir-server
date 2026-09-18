// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Xunit;

namespace Microsoft.Health.Extensions.Xunit.TestAssets
{
    /// <summary>
    /// A test class with class-level fixture argument sets, mirroring the real E2E test classes. Each method is
    /// expanded into one test class variant per closed argument set, and <see cref="OverridingMethod"/> narrows
    /// the sets at the method level.
    /// </summary>
    /// <remarks>
    /// These methods are discovery fixtures, not real tests. Their bodies are intentionally empty.
    /// </remarks>
    [SampleFixtureArgumentSets(SampleDataStore.All, SampleFormat.FormatA)]
    public class VariantTests : IClassFixture<SampleFixture>
    {
        private readonly SampleFixture _fixture;

        public VariantTests(SampleFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public void VariantFactOne()
        {
            Assert.NotNull(_fixture);
        }

        [Fact]
        public void VariantFactTwo()
        {
            Assert.NotNull(_fixture);
        }

        [Fact]
        public void VariantFactThree()
        {
            Assert.NotNull(_fixture);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        public void VariantTheory(int value)
        {
            _ = value;
        }

        /// <summary>
        /// Overrides the format dimension at the method level, so this method expands into more variants than the
        /// rest of the class. All of them must still land in the same shard.
        /// </summary>
        /// <param name="value">An arbitrary theory argument.</param>
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [SampleFixtureArgumentSets(formats: SampleFormat.All)]
        public void OverridingMethod(int value)
        {
            _ = value;
        }
    }
}
