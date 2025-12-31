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
                Definition = new ResourceReference
                {
                    Url = new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"),
                },
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
        public void GivenAComponentWithEmptyDefinition_WhenCallingGetComponentDefinitionUri_ThenEmptyUriIsReturned()
        {
            // Arrange
            var component = new SearchParameter.ComponentComponent
            {
                Definition = new ResourceReference
                {
                    Url = new Uri(string.Empty, UriKind.Relative),
                },
            };

            // Act
            Uri result = component.GetComponentDefinitionUri();

            // Assert
            Assert.NotNull(result);
            Assert.Equal(string.Empty, result.OriginalString);
        }

        [Fact]
        public void GivenAComponentWithInvalidUri_WhenCallingGetComponentDefinitionUri_ThenUriFormatExceptionIsThrown()
        {
            // Arrange & Act & Assert
            Assert.Throws<UriFormatException>(() =>
            {
                var component = new SearchParameter.ComponentComponent
                {
                    Definition = new ResourceReference
                    {
                        Url = new Uri("not a valid uri"),
                    },
                };
            });
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
                Definition = new ResourceReference
                {
                    Url = new Uri(definitionUrl),
                },
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
            ResourceReference result = reference.GetComponentDefinition();

            // Assert
            Assert.NotNull(result);
            Assert.NotNull(result.Url);
            Assert.Equal(urlString, result.Url.OriginalString);
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
            ResourceReference result = reference.GetComponentDefinition();

            // Assert
            Assert.NotNull(result);
            Assert.Null(result.Url);
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
            ResourceReference result = reference.GetComponentDefinition();

            // Assert
            Assert.NotNull(result);
            Assert.NotNull(result.Url);
            Assert.Empty(result.Url.OriginalString);
        }
    }
}
