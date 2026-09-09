// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
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
        public void GivenEvidenceFromReturnedResources_WhenAssigningRanks_ThenRanksAreDenseAcrossResources()
        {
            // Arrange
            var canonical = new Uri("https://example.org/fhir/SearchParameter/semantic-text");
            IReadOnlyList<IReadOnlyList<SemanticSearchEvidence>> evidenceByResource = new[]
            {
                new[]
                {
                    CreateEvidence("Resource one best", chunkOrdinal: 0, score: 0.90m, canonical),
                    CreateEvidence("Resource one second", chunkOrdinal: 1, score: 0.70m, canonical),
                },
                new[]
                {
                    CreateEvidence("Resource two best", chunkOrdinal: 0, score: 0.80m, canonical),
                    CreateEvidence("Resource two second", chunkOrdinal: 1, score: 0.70m, canonical),
                },
            };

            // Act
            IReadOnlyList<IReadOnlyList<SemanticSearchEvidence>> ranked = SemanticSearchEvidenceRanker.AssignRanks(evidenceByResource);

            // Assert
            Assert.Equal(new int?[] { 1, 3 }, ranked[0].Select(evidence => evidence.Rank));
            Assert.Equal(new int?[] { 2, 4 }, ranked[1].Select(evidence => evidence.Rank));
            Assert.All(evidenceByResource.SelectMany(evidence => evidence), evidence => Assert.Null(evidence.Rank));
        }

        [Fact]
        public void GivenWitnessEvidence_WhenAssigningRank_ThenWitnessIsPreserved()
        {
            var evidence = new SemanticSearchEvidence(
                "Matched Binary passage",
                chunkOrdinal: 0,
                score: 0.9m,
                new Uri("https://example.org/fhir/SearchParameter/document-reference-semantic"),
                "Binary/source/_history/2",
                "Binary.data",
                witnessReference: "DocumentReference/document/_history/3");

            SemanticSearchEvidence ranked = Assert.Single(Assert.Single(
                SemanticSearchEvidenceRanker.AssignRanks(new[] { new[] { evidence } })));

            Assert.Equal("DocumentReference/document/_history/3", ranked.WitnessReference);
            Assert.Equal(1, ranked.Rank);
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

        private static SemanticSearchEvidence CreateEvidence(string text, int chunkOrdinal, decimal score, Uri canonical)
        {
            return new SemanticSearchEvidence(
                text,
                chunkOrdinal,
                score,
                canonical,
                $"Observation/{chunkOrdinal}",
                "Observation.note.text");
        }
    }
}
