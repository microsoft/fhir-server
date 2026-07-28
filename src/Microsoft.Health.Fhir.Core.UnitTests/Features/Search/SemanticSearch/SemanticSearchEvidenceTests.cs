// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.SemanticSearch
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public sealed class SemanticSearchEvidenceTests
    {
        [Fact]
        public void GivenCompleteEvidence_WhenCreated_ThenPassageAndProvenanceArePreserved()
        {
            // Arrange
            var canonical = new Uri("https://example.org/fhir/SearchParameter/observation-semantic-text");

            // Act
            var evidence = new SemanticSearchEvidence(
                "The patient became short of breath while climbing stairs.",
                chunkOrdinal: 2,
                canonical,
                "Observation/123/_history/4",
                "Observation.note.text");

            // Assert
            Assert.Equal("The patient became short of breath while climbing stairs.", evidence.Text);
            Assert.Equal(2, evidence.ChunkOrdinal);
            Assert.Same(canonical, evidence.SearchParameterCanonical);
            Assert.Equal("Observation/123/_history/4", evidence.SourceReference);
            Assert.Equal("Observation.note.text", evidence.SourcePath);
            Assert.Equal("http://microsoft.com/fhir/StructureDefinition/semantic-search-evidence", SemanticSearchEvidence.ExtensionUrl);
        }

        [Fact]
        public void GivenRelativeSearchParameterCanonical_WhenCreatingEvidence_ThenArgumentExceptionIsThrown()
        {
            // Arrange
            var relativeCanonical = new Uri("SearchParameter/observation-semantic-text", UriKind.Relative);

            // Act
            Action create = () => new SemanticSearchEvidence(
                "Matched passage",
                chunkOrdinal: 0,
                relativeCanonical,
                "Observation/123/_history/4",
                "Observation.note.text");

            // Assert
            Assert.Throws<ArgumentException>(create);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void GivenMissingPassageText_WhenCreatingEvidence_ThenArgumentExceptionIsThrown(string text)
        {
            // Arrange
            var canonical = new Uri("https://example.org/fhir/SearchParameter/observation-semantic-text");

            // Act
            Action create = () => new SemanticSearchEvidence(
                text,
                chunkOrdinal: 0,
                canonical,
                "Observation/123/_history/4",
                "Observation.note.text");

            // Assert
            Assert.ThrowsAny<ArgumentException>(create);
        }
    }
}
