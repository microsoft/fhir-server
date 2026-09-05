// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class FhirQueryNormalizerTests
    {
        [Fact]
        public void GivenSameParameterNamesInDifferentOrder_WhenNormalized_ThenRepresentationsAreIdentical()
        {
            // Arrange
            var first = new[]
            {
                Tuple.Create("name", "Alice"),
                Tuple.Create("birthdate", "gt2000-01-01"),
            };
            var second = new[]
            {
                Tuple.Create("birthdate", "lt1990-01-01"),
                Tuple.Create("name", "Bob"),
            };

            // Act
            string firstResult = FhirQueryNormalizer.Normalize("Patient", first);
            string secondResult = FhirQueryNormalizer.Normalize("Patient", second);

            // Assert
            Assert.Equal("Patient?birthdate&name", firstResult);
            Assert.Equal(firstResult, secondResult);
            Assert.DoesNotContain("Alice", firstResult, StringComparison.Ordinal);
            Assert.DoesNotContain("Bob", secondResult, StringComparison.Ordinal);
            Assert.DoesNotContain("2000-01-01", firstResult, StringComparison.Ordinal);
            Assert.DoesNotContain("1990-01-01", secondResult, StringComparison.Ordinal);
        }

        [Fact]
        public void GivenCommentDelimitersAndControlCharacters_WhenNormalized_ThenOutputIsSafe()
        {
            // Arrange
            var queryParameters = new[]
            {
                Tuple.Create("subject.name:exact*/--\r\n\u0001", "recognizable-value"),
            };

            // Act
            string result = FhirQueryNormalizer.Normalize("Patient*/\r\n", queryParameters);

            // Assert
            Assert.Equal("Patient____?subject.name:exact_______", result);
            Assert.DoesNotContain("*/", result, StringComparison.Ordinal);
            Assert.DoesNotContain("--", result, StringComparison.Ordinal);
            Assert.DoesNotContain('\r', result);
            Assert.DoesNotContain('\n', result);
            Assert.DoesNotContain('\u0001', result);
            Assert.DoesNotContain("recognizable-value", result, StringComparison.Ordinal);
        }

        [Fact]
        public void GivenNormalizedQueryExceedsMaximumLength_WhenNormalized_ThenOutputIsDeterministicallyTruncated()
        {
            // Arrange
            var queryParameters = new[]
            {
                Tuple.Create(new string('a', FhirQueryNormalizer.MaximumLength), "secret"),
            };

            // Act
            string firstResult = FhirQueryNormalizer.Normalize("Patient", queryParameters);
            string secondResult = FhirQueryNormalizer.Normalize("Patient", queryParameters);

            // Assert
            Assert.Equal(FhirQueryNormalizer.MaximumLength, firstResult.Length);
            Assert.EndsWith("~", firstResult, StringComparison.Ordinal);
            Assert.Equal(firstResult, secondResult);
            Assert.DoesNotContain("secret", firstResult, StringComparison.Ordinal);
        }

        [Fact]
        public void GivenRepeatedModifiedChainedAndControlParameters_WhenNormalized_ThenSyntaxAndMultiplicityArePreserved()
        {
            // Arrange
            var queryParameters = new[]
            {
                Tuple.Create("subject:Patient.name:exact", "Alice"),
                Tuple.Create("_has:Observation:patient:code", "1234-5"),
                Tuple.Create("_include", "Patient:general-practitioner"),
                Tuple.Create("_sort", "-birthdate"),
                Tuple.Create("name", "Alice"),
                Tuple.Create("name", "Bob"),
                Tuple.Create("_count", "25"),
            };

            // Act
            string result = FhirQueryNormalizer.Normalize("Patient", queryParameters);

            // Assert
            Assert.Equal(
                "Patient?_count&_has:Observation:patient:code&_include&_sort&name&name&subject:Patient.name:exact",
                result);
        }

        [Fact]
        public void GivenNoParameters_WhenNormalized_ThenOnlyResourceTypeIsReturned()
        {
            // Act
            string result = FhirQueryNormalizer.Normalize("Patient", []);

            // Assert
            Assert.Equal("Patient", result);
        }

        [Theory]
        [InlineData(null, "Patient", false, "Patient?name")]
        [InlineData(null, null, false, "Resource?name")]
        [InlineData(null, "", false, "Resource?name")]
        [InlineData(null, " ", false, "Resource?name")]
        [InlineData(null, "Patient", true, "Patient/_history?name")]
        [InlineData(null, null, true, "Resource/_history?name")]
        [InlineData("", "Patient", false, "Patient?name")]
        [InlineData(" ", "Patient", false, "Patient?name")]
        [InlineData("Patient", "Observation", false, "Patient/$compartment/Observation?name")]
        [InlineData("Patient", null, false, "Patient/$compartment/Resource?name")]
        public void GivenSearchContext_WhenNormalized_ThenSearchScopeIsIdentified(
            string compartmentType,
            string resourceType,
            bool isHistory,
            string expected)
        {
            // Arrange
            var queryParameters = new[]
            {
                Tuple.Create("name", "Alice"),
            };

            // Act
            string result = FhirQueryNormalizer.Normalize(resourceType, queryParameters, compartmentType, isHistory);

            // Assert
            Assert.Equal(expected, result);
        }
    }
}
