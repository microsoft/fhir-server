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
        public void GivenAFhirServerInstanceConfiguration_WhenInitializeBaseUriCalled_ThenBaseUriIsStoredAndReturnsTrue()
        {
            // Arrange
            var config = new FhirServerInstanceConfiguration();
            var baseUriString = "https://localhost/fhir/";

            // Act
            bool result = config.InitializeBaseUri(baseUriString);

            // Assert
            Assert.True(result);
            Assert.Equal(new Uri(baseUriString), config.BaseUri);
        }

        [Fact]
        public void GivenAFhirServerInstanceConfiguration_WhenBaseUriInitializedMultipleTimes_ThenFirstValueWinsAndBothCallsReturnTrue()
        {
            // Arrange
            var config = new FhirServerInstanceConfiguration();
            var baseUriString1 = "https://localhost1/fhir/";
            var baseUriString2 = "https://localhost2/fhir/";

            // Act
            bool firstResult = config.InitializeBaseUri(baseUriString1);
            bool secondResult = config.InitializeBaseUri(baseUriString2);

            // Assert
            Assert.True(firstResult);
            Assert.True(secondResult); // Second call also returns true because BaseUri is initialized
            Assert.Equal(new Uri(baseUriString1), config.BaseUri);
        }

        [Fact]
        public async Task GivenAFhirServerInstanceConfiguration_WhenBaseUriInitializedConcurrently_ThenOnlyOneSucceedsButAllReturnTrue()
        {
            // Arrange
            var config = new FhirServerInstanceConfiguration();
            var baseUriStrings = new[] { "https://localhost1/fhir/", "https://localhost2/fhir/", "https://localhost3/fhir/" };
            var tasks = new Task<bool>[baseUriStrings.Length];

            // Act - Initialize concurrently from multiple threads
            for (int i = 0; i < baseUriStrings.Length; i++)
            {
                int index = i;
                tasks[i] = Task.Run(() => config.InitializeBaseUri(baseUriStrings[index]));
            }

            bool[] results = await Task.WhenAll(tasks);

            // Assert - All calls should return true because the value is initialized
            Assert.All(results, r => Assert.True(r));
            Assert.True(Array.Exists(baseUriStrings, urlString => new Uri(urlString) == config.BaseUri), "Stored BaseUri should be one of the attempted values");
        }

        [Fact]
        public void GivenAFhirServerInstanceConfiguration_WhenNotInitialized_ThenBaseUriIsNull()
        {
            // Arrange
            var config = new FhirServerInstanceConfiguration();

            // Assert
            Assert.Null(config.BaseUri);
        }

        [Fact]
        public void GivenAFhirServerInstanceConfiguration_WhenInitializedWithInvalidBaseUri_ThenReturnsFalseAndRemainsUninitialized()
        {
            // Arrange
            var config = new FhirServerInstanceConfiguration();
            var invalidUrlString = "not a valid uri";

            // Act
            bool result = config.InitializeBaseUri(invalidUrlString);

            // Assert
            Assert.False(result);
            Assert.Null(config.BaseUri);
        }
    }
}
