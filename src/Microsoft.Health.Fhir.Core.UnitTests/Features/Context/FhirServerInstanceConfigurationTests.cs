// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Context
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class FhirServerInstanceConfigurationTests
    {
        [Fact]
        public void GivenAFhirServerInstanceConfiguration_WhenInitializedOnce_ThenValuesAreStored()
        {
            // Arrange
            var config = new FhirServerInstanceConfiguration();
            var baseUriString = "https://localhost/fhir/";

            // Act
            config.Initialize(baseUriString);

            // Assert
            Assert.True(config.IsInitialized);
            Assert.Equal(new Uri(baseUriString), config.BaseUri);
        }

        [Fact]
        public void GivenAFhirServerInstanceConfiguration_WhenInitializedWithString_ThenValuesAreStored()
        {
            // Arrange
            var config = new FhirServerInstanceConfiguration();
            var baseUriString = "https://localhost/fhir/";

            // Act
            config.Initialize(baseUriString);

            // Assert
            Assert.True(config.IsInitialized);
            Assert.Equal(new Uri(baseUriString), config.BaseUri);
        }

        [Fact]
        public void GivenAFhirServerInstanceConfiguration_WhenInitializedMultipleTimes_ThenFirstValueWins()
        {
            // Arrange
            var config = new FhirServerInstanceConfiguration();
            var baseUriString1 = "https://localhost1/fhir/";
            var baseUriString2 = "https://localhost2/fhir/";

            // Act
            config.Initialize(baseUriString1);
            config.Initialize(baseUriString2);

            // Assert
            Assert.True(config.IsInitialized);
            Assert.Equal(new Uri(baseUriString1), config.BaseUri);
        }

        [Fact]
        public async Task GivenAFhirServerInstanceConfiguration_WhenInitializedConcurrently_ThenOnlyOneSucceeds()
        {
            // Arrange
            var config = new FhirServerInstanceConfiguration();
            var baseUriStrings = new[] { "https://localhost1/fhir/", "https://localhost2/fhir/", "https://localhost3/fhir/" };
            var tasks = new Task[baseUriStrings.Length];

            // Act - Initialize concurrently from multiple threads
            for (int i = 0; i < baseUriStrings.Length; i++)
            {
                int index = i;
                tasks[i] = Task.Run(() => config.Initialize(baseUriStrings[index]));
            }

            await Task.WhenAll(tasks);

            // Assert - Only one of the base URIs should be stored
            Assert.True(config.IsInitialized);
            Assert.True(Array.Exists(baseUriStrings, urlString => new Uri(urlString) == config.BaseUri), "Stored BaseUri should be one of the attempted values");
        }

        [Fact]
        public void GivenAFhirServerInstanceConfiguration_WhenNotInitialized_ThenIsInitializedIsFalse()
        {
            // Arrange
            var config = new FhirServerInstanceConfiguration();

            // Assert
            Assert.False(config.IsInitialized);
            Assert.Null(config.BaseUri);
        }

        [Fact]
        public void GivenAFhirServerInstanceConfiguration_WhenInitializedWithInvalidUri_ThenNoExceptionThrown()
        {
            // Arrange
            var config = new FhirServerInstanceConfiguration();
            var invalidUrlString = "not a valid uri";

            // Act - Should not throw
            config.Initialize(invalidUrlString);

            // Assert - Should remain uninitialized since URI parsing failed
            Assert.False(config.IsInitialized);
        }

        [Fact]
        public void GivenAFhirServerInstanceConfiguration_WhenInitializedWithVanityUrl_ThenVanityUrlIsStored()
        {
            // Arrange
            var config = new FhirServerInstanceConfiguration();
            var baseUriString = "https://localhost/fhir/";
            var vanityUrlString = "https://custom.example.com/fhir/";

            // Act
            config.Initialize(baseUriString, vanityUrlString);

            // Assert
            Assert.True(config.IsInitialized);
            Assert.Equal(new Uri(baseUriString), config.BaseUri);
            Assert.Equal(new Uri(vanityUrlString), config.VanityUrl);
        }

        [Fact]
        public void GivenAFhirServerInstanceConfiguration_WhenInitializedWithoutVanityUrl_ThenVanityUrlIsNull()
        {
            // Arrange
            var config = new FhirServerInstanceConfiguration();
            var baseUriString = "https://localhost/fhir/";

            // Act
            config.Initialize(baseUriString);

            // Assert
            Assert.True(config.IsInitialized);
            Assert.Equal(new Uri(baseUriString), config.BaseUri);
            Assert.Null(config.VanityUrl);
        }

        [Fact]
        public void GivenAFhirServerInstanceConfiguration_WhenInitializedWithInvalidVanityUrlString_ThenVanityUrlRemainsNull()
        {
            // Arrange
            var config = new FhirServerInstanceConfiguration();
            var baseUriString = "https://localhost/fhir/";
            var invalidVanityUrlString = "not a valid uri";

            // Act - Should not throw
            config.Initialize(baseUriString, invalidVanityUrlString);

            // Assert - Should initialize with base URI but vanity URL remains null due to invalid string
            Assert.True(config.IsInitialized);
            Assert.Equal(new Uri(baseUriString), config.BaseUri);
            Assert.Null(config.VanityUrl);
        }
    }
}
