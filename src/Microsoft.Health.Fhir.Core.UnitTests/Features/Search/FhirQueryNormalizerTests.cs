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
            string[] first = ["name", "birthdate"];
            string[] second = ["birthdate", "name"];

            // Act
            string firstResult = FhirQueryNormalizer.Normalize("Patient", first);
            string secondResult = FhirQueryNormalizer.Normalize("Patient", second);

            // Assert
            Assert.Equal("Patient?birthdate&name", firstResult);
            Assert.Equal(firstResult, secondResult);
        }

        [Fact]
        public void GivenCommentDelimitersAndControlCharacters_WhenNormalized_ThenOutputIsSafe()
        {
            // Arrange
            string[] parameterNames = ["subject.name:exact*/--\r\n\u0001"];

            // Act
            string result = FhirQueryNormalizer.Normalize("Patient*/\r\n", parameterNames);

            // Assert
            Assert.Equal("Patient____?subject.name:exact_______", result);
            Assert.DoesNotContain("*/", result, StringComparison.Ordinal);
            Assert.DoesNotContain("--", result, StringComparison.Ordinal);
            Assert.DoesNotContain('\r', result);
            Assert.DoesNotContain('\n', result);
            Assert.DoesNotContain('\u0001', result);
        }

        [Fact]
        public void GivenNormalizedQueryExceedsMaximumLength_WhenNormalized_ThenOutputIsDeterministicallyTruncated()
        {
            // Arrange
            string[] parameterNames = [new string('a', FhirQueryNormalizer.MaximumLength)];

            // Act
            string firstResult = FhirQueryNormalizer.Normalize("Patient", parameterNames);
            string secondResult = FhirQueryNormalizer.Normalize("Patient", parameterNames);

            // Assert
            Assert.Equal(FhirQueryNormalizer.MaximumLength, firstResult.Length);
            Assert.EndsWith("~", firstResult, StringComparison.Ordinal);
            Assert.Equal(firstResult, secondResult);
        }

        [Fact]
        public void GivenRepeatedModifiedChainedAndControlParameters_WhenNormalized_ThenSyntaxAndMultiplicityArePreserved()
        {
            // Arrange
            string[] parameterNames =
            [
                "subject:Patient.name:exact",
                "_has:Observation:patient:code",
                "_include",
                "_sort",
                "name",
                "name",
                "_count",
            ];

            // Act
            string result = FhirQueryNormalizer.Normalize("Patient", parameterNames);

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
            string[] parameterNames = ["name"];

            // Act
            string result = FhirQueryNormalizer.Normalize(resourceType, parameterNames, compartmentType, isHistory);

            // Assert
            Assert.Equal(expected, result);
        }
    }
}
