// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Xunit;

[assembly: TestFramework(typeof(Microsoft.Health.Extensions.Xunit.CustomXunitTestFramework))]

namespace Microsoft.Health.Extensions.Xunit.UnitTests
{
    [FixtureArgumentSetsTests.TestFixtureArguments(FixtureArgumentSetsTests.DataStore.All, FixtureArgumentSetsTests.Format.All)]
    public class FixtureArgumentSetsTests : IClassFixture<FixtureArgumentSetsTests.VariantFixture>
    {
        private readonly VariantFixture _fixture;

        public FixtureArgumentSetsTests(VariantFixture fixture)
        {
            _fixture = fixture;
        }

        [Flags]
        public enum DataStore
        {
            SqlServer = 1,
            CosmosDb = 2,
            All = SqlServer | CosmosDb,
        }

        [Flags]
        public enum Format
        {
            Json = 1,
            Xml = 2,
            All = Json | Xml,
        }

        [Fact]
        public void GivenClassVariants_FixtureArgumentsMatchTestTraits()
        {
            var traits = TestContext.Current.TestCase.Traits;

            Assert.Equal(_fixture.DataStore.ToString(), Assert.Single(traits[nameof(DataStore)]));
            Assert.Equal(_fixture.Format.ToString(), Assert.Single(traits[nameof(Format)]));
            Assert.Contains($"({_fixture.DataStore}, {_fixture.Format})", TestContext.Current.TestCase.TestCaseDisplayName);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [TestFixtureArguments(DataStore.SqlServer)]
        public void GivenMethodOverride_FixtureUsesOverriddenDataStoreAndInheritedFormat(int value)
        {
            var traits = TestContext.Current.TestCase.Traits;

            Assert.InRange(value, 1, 2);
            Assert.Equal(DataStore.SqlServer, _fixture.DataStore);
            Assert.Equal(_fixture.DataStore.ToString(), Assert.Single(traits[nameof(DataStore)]));
            Assert.Equal(_fixture.Format.ToString(), Assert.Single(traits[nameof(Format)]));
        }

        public sealed class TestFixtureArgumentsAttribute : FixtureArgumentSetsAttribute
        {
            public TestFixtureArgumentsAttribute(DataStore dataStore = 0, Format format = 0)
                : base(dataStore, format)
            {
            }
        }

        public sealed class VariantFixture
        {
            public VariantFixture(DataStore dataStore, Format format)
            {
                DataStore = dataStore;
                Format = format;
            }

            public DataStore DataStore { get; }

            public Format Format { get; }
        }
    }
}
