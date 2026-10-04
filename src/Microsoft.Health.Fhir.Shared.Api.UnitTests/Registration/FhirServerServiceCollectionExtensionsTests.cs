// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Registration
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Validate)]
    [Trait(Traits.Category, Categories.Web)]
    public class FhirServerServiceCollectionExtensionsTests
    {
        [Theory]
        [InlineData(-1)]
        [InlineData(0)]
        [InlineData(63)]
        [InlineData(129)]
        [InlineData(256)]
        public void GivenAMaxResourceIdLengthOutsideTheSupportedRange_WhenAddingTheFhirServer_ThenAnInvalidOperationExceptionIsThrown(int maxResourceIdLength)
        {
            // Arrange
            var services = new ServiceCollection();

            // Act
            var exception = Assert.Throws<InvalidOperationException>(
                () => services.AddFhirServer(
                    configurationRoot: null,
                    configureAction: configuration => configuration.CoreFeatures.MaxResourceIdLength = maxResourceIdLength));

            // Assert
            Assert.Contains("FhirServer:CoreFeatures:MaxResourceIdLength", exception.Message, StringComparison.Ordinal);
            Assert.Contains("between 64 and 128", exception.Message, StringComparison.Ordinal);
            Assert.Contains(maxResourceIdLength.ToString(CultureInfo.InvariantCulture), exception.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("64", 64)]
        [InlineData("100", 100)]
        [InlineData("128", 128)]
        public void GivenAMaxResourceIdLengthInConfiguration_WhenAddingTheFhirServer_ThenTheValueIsBound(string configuredValue, int expected)
        {
            // Arrange
            var services = new ServiceCollection();
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string> { ["FhirServer:CoreFeatures:MaxResourceIdLength"] = configuredValue })
                .Build();

            // Act
            services.AddFhirServer(configuration);

            // Assert
            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.Equal(expected, provider.GetRequiredService<IOptions<CoreFeatureConfiguration>>().Value.MaxResourceIdLength);
        }

        [Fact]
        public void GivenNoMaxResourceIdLengthInConfiguration_WhenAddingTheFhirServer_ThenTheDefaultIs64()
        {
            // Arrange
            var services = new ServiceCollection();

            // Act
            services.AddFhirServer(new ConfigurationBuilder().Build());

            // Assert
            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.Equal(64, provider.GetRequiredService<IOptions<CoreFeatureConfiguration>>().Value.MaxResourceIdLength);
        }

        [Fact]
        public void GivenAnOutOfRangeMaxResourceIdLengthInConfiguration_WhenAddingTheFhirServer_ThenAnInvalidOperationExceptionIsThrown()
        {
            // Arrange
            var services = new ServiceCollection();
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string> { ["FhirServer:CoreFeatures:MaxResourceIdLength"] = "129" })
                .Build();

            // Act and Assert
            Assert.Throws<InvalidOperationException>(() => services.AddFhirServer(configuration));
        }
    }
}
