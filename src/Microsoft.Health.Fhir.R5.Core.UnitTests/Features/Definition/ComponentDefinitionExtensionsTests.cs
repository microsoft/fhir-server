// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Definition
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class ComponentDefinitionExtensionsTests
    {
        [Fact]
        public void GivenAComponentWithValidDefinition_WhenCallingGetComponentDefinitionUri_ThenUriIsReturned()
        {
            // Arrange
            var component = new SearchParameter.ComponentComponent
            {
                Definition = "http://hl7.org/fhir/SearchParameter/Patient-name",
            };

            // Act
            Uri result = component.GetComponentDefinitionUri();

            // Assert
            Assert.NotNull(result);
            Assert.Equal("http://hl7.org/fhir/SearchParameter/Patient-name", result.OriginalString);
        }

        [Fact]
        public void GivenAComponentWithNullDefinition_WhenCallingGetComponentDefinitionUri_ThenNullIsReturned()
        {
            // Arrange
            var component = new SearchParameter.ComponentComponent
            {
                Definition = null,
            };

            // Act
            Uri result = component.GetComponentDefinitionUri();

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void GivenAComponentWithEmptyDefinition_WhenCallingGetComponentDefinitionUri_ThenNullIsReturned()
        {
            // Arrange
            var component = new SearchParameter.ComponentComponent
            {
                Definition = string.Empty,
            };

            // Act
            Uri result = component.GetComponentDefinitionUri();

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void GivenAComponentWithInvalidUri_WhenCallingGetComponentDefinitionUri_ThenUriFormatExceptionIsThrown()
        {
            // Arrange
            var component = new SearchParameter.ComponentComponent
            {
                Definition = "not a valid uri",
            };

            // Act & Assert
            Assert.Throws<UriFormatException>(() => component.GetComponentDefinitionUri());
        }

        [Theory]
        [InlineData("http://hl7.org/fhir/SearchParameter/Patient-name")]
        [InlineData("http://hl7.org/fhir/SearchParameter/Observation-code")]
        [InlineData("https://example.com/custom/search/param")]
        public void GivenVariousValidDefinitions_WhenCallingGetComponentDefinitionUri_ThenCorrectUriIsReturned(string definitionUrl)
        {
            // Arrange
            var component = new SearchParameter.ComponentComponent
            {
                Definition = definitionUrl,
            };

            // Act
            Uri result = component.GetComponentDefinitionUri();

            // Assert - whitespace should return null, others should return valid URI
            if (string.IsNullOrWhiteSpace(definitionUrl))
            {
                Assert.Null(result);
            }
            else
            {
                Assert.NotNull(result);
                Assert.Equal(definitionUrl, result.OriginalString);
            }
        }

        [Theory]
        [InlineData("http://hl7.org/fhir/SearchParameter/Patient-birthdate")]
        [InlineData("http://example.org/fhir/SearchParameter/custom-param")]
        public void GivenResourceReferenceWithVariousUrls_WhenCallingGetComponentDefinition_ThenCorrectStringIsReturned(string urlString)
        {
            // Arrange
            var reference = new ResourceReference
            {
                Url = new Uri(urlString),
            };

            // Act
            string result = reference.GetComponentDefinition();

            // Assert
            Assert.NotNull(result);
            Assert.Equal(urlString, result);
        }

        [Fact]
        public void GivenAResourceReferenceWithNullUrl_WhenCallingGetComponentDefinition_ThenNullIsReturned()
        {
            // Arrange
            var reference = new ResourceReference
            {
                Url = null,
            };

            // Act
            string result = reference.GetComponentDefinition();

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void GivenAResourceReferenceWithEmptyUrl_WhenCallingGetComponentDefinition_ThenEmptyStringIsReturned()
        {
            // Arrange
            var expectedUrl = string.Empty;
            var reference = new ResourceReference
            {
                Url = new Uri(expectedUrl, UriKind.Relative),
            };

            // Act
            string result = reference.GetComponentDefinition();

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }
    }
}
