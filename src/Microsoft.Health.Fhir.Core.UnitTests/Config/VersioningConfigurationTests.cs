// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Config
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public sealed class VersioningConfigurationTests
    {
        /// <summary>
        /// Verifies that mixed-case versioning policy values are normalized to the expected lowercase policy constants,
        /// and that already normalized lowercase values are preserved.
        /// </summary>
        /// <param name="input">The configured default versioning policy value.</param>
        /// <param name="expected">The normalized versioning policy value that should be stored.</param>
        [Theory]
        [InlineData("Versioned", ResourceVersionPolicy.Versioned)]
        [InlineData("No-Version", ResourceVersionPolicy.NoVersion)]
        [InlineData("Versioned-Update", ResourceVersionPolicy.VersionedUpdate)]
        [InlineData(ResourceVersionPolicy.Versioned, ResourceVersionPolicy.Versioned)]
        [InlineData(ResourceVersionPolicy.NoVersion, ResourceVersionPolicy.NoVersion)]
        [InlineData(ResourceVersionPolicy.VersionedUpdate, ResourceVersionPolicy.VersionedUpdate)]
        public void GivenAVersioningConfiguration_WhenSettingDefault_ThenValueIsNormalizedToLowercase(string input, string expected)
        {
            VersioningConfiguration configuration = new()
            {
                Default = input,
            };

            Assert.Equal(expected, configuration.Default);
        }

        /// <summary>
        /// Verifies that a new versioning configuration defaults to the versioned policy.
        /// </summary>
        [Fact]
        public void GivenAVersioningConfiguration_WhenCreated_ThenDefaultValueIsVersioned()
        {
            VersioningConfiguration configuration = new();

            Assert.Equal(ResourceVersionPolicy.Versioned, configuration.Default);
        }

        /// <summary>
        /// Ensures null versioning policy values are handled gracefully by falling back to the default versioned policy, rather than throwing an exception.
        /// If we start throwing an exception for null default value, this test will need to change.
        /// </summary>
        [Fact]
        public void GivenAVersioningConfiguration_WhenDefaultIsSetToNull_ThenDefaultFallsBackToVersioned()
        {
            VersioningConfiguration configuration = new()
            {
                Default = null!,
            };

            Assert.Equal(ResourceVersionPolicy.Versioned, configuration.Default);
        }
    }
}
