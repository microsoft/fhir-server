// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch
{
    /// <summary>
    /// Describes the exact source passage that supports one semantic-search result.
    /// </summary>
    public sealed class SemanticSearchEvidence
    {
        /// <summary>
        /// Gets the canonical URL of the extension carried on <c>Bundle.entry.search</c>.
        /// </summary>
        public const string ExtensionUrl = "http://microsoft.com/fhir/StructureDefinition/semantic-search-evidence";

        /// <summary>
        /// Gets the nested extension URL for the matched passage text.
        /// </summary>
        public const string TextExtensionUrl = "text";

        /// <summary>
        /// Gets the nested extension URL for the passage ordinal.
        /// </summary>
        public const string ChunkOrdinalExtensionUrl = "chunkOrdinal";

        /// <summary>
        /// Gets the nested extension URL for the one-based relevance rank across evidence on the current response page.
        /// </summary>
        public const string RankExtensionUrl = "rank";

        /// <summary>
        /// Gets the nested extension URL for the normalized passage relevance score.
        /// </summary>
        public const string ScoreExtensionUrl = "score";

        /// <summary>
        /// Gets the nested extension URL for the vector SearchParameter canonical.
        /// </summary>
        public const string SearchParameterExtensionUrl = "searchParameter";

        /// <summary>
        /// Gets the nested extension URL for the FHIR resource containing the source text.
        /// </summary>
        public const string SourceExtensionUrl = "source";

        /// <summary>
        /// Gets the nested extension URL for the source element path.
        /// </summary>
        public const string SourcePathExtensionUrl = "sourcePath";

        /// <summary>
        /// Initializes a new instance of the <see cref="SemanticSearchEvidence"/> class.
        /// </summary>
        /// <param name="text">The exact passage text represented by the matched embedding.</param>
        /// <param name="chunkOrdinal">The zero-based ordinal of the passage within the indexed text.</param>
        /// <param name="searchParameterCanonical">The canonical URL of the SearchParameter that selected the text.</param>
        /// <param name="sourceReference">The FHIR reference to the resource containing the source text.</param>
        /// <param name="sourcePath">The path of the source element within the referenced resource.</param>
        public SemanticSearchEvidence(
            string text,
            int chunkOrdinal,
            Uri searchParameterCanonical,
            string sourceReference,
            string sourcePath)
            : this(text, chunkOrdinal, score: null, searchParameterCanonical, sourceReference, sourcePath)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SemanticSearchEvidence"/> class.
        /// </summary>
        /// <param name="text">The exact passage text represented by the matched embedding.</param>
        /// <param name="chunkOrdinal">The zero-based ordinal of the passage within the indexed text.</param>
        /// <param name="score">The normalized passage relevance score, where higher is more relevant.</param>
        /// <param name="searchParameterCanonical">The canonical URL of the SearchParameter that selected the text.</param>
        /// <param name="sourceReference">The FHIR reference to the resource containing the source text.</param>
        /// <param name="sourcePath">The path of the source element within the referenced resource.</param>
        /// <param name="rank">The optional one-based relevance rank across evidence on the current response page.</param>
        public SemanticSearchEvidence(
            string text,
            int chunkOrdinal,
            decimal? score,
            Uri searchParameterCanonical,
            string sourceReference,
            string sourcePath,
            int? rank = null)
        {
            EnsureArg.IsNotNullOrWhiteSpace(text, nameof(text));
            EnsureArg.IsGte(chunkOrdinal, 0, nameof(chunkOrdinal));
            EnsureArg.IsNotNull(searchParameterCanonical, nameof(searchParameterCanonical));
            EnsureArg.IsNotNullOrWhiteSpace(sourceReference, nameof(sourceReference));
            EnsureArg.IsNotNullOrWhiteSpace(sourcePath, nameof(sourcePath));

            if (score is < 0 or > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(score), score, "The semantic evidence score must be between 0 and 1.");
            }

            if (rank is <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(rank), rank, "The semantic evidence rank must be greater than zero.");
            }

            if (!searchParameterCanonical.IsAbsoluteUri)
            {
                throw new ArgumentException("The SearchParameter canonical URL must be absolute.", nameof(searchParameterCanonical));
            }

            Text = text;
            ChunkOrdinal = chunkOrdinal;
            Rank = rank;
            Score = score;
            SearchParameterCanonical = searchParameterCanonical;
            SourceReference = sourceReference;
            SourcePath = sourcePath;
        }

        /// <summary>
        /// Gets the exact passage text represented by the matched embedding.
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// Gets the zero-based ordinal of the passage within the indexed text.
        /// </summary>
        public int ChunkOrdinal { get; }

        /// <summary>
        /// Gets the optional one-based relevance rank across all evidence attached to resources on the current response page.
        /// </summary>
        public int? Rank { get; }

        /// <summary>
        /// Gets the normalized passage relevance score, where higher is more relevant.
        /// </summary>
        public decimal? Score { get; }

        /// <summary>
        /// Gets the canonical URL of the SearchParameter that selected the text.
        /// </summary>
        public Uri SearchParameterCanonical { get; }

        /// <summary>
        /// Gets the FHIR reference to the resource containing the source text.
        /// </summary>
        public string SourceReference { get; }

        /// <summary>
        /// Gets the path of the source element within the referenced resource.
        /// </summary>
        public string SourcePath { get; }

        /// <summary>
        /// Creates a copy with the specified page-scoped rank.
        /// </summary>
        /// <param name="rank">The one-based rank across evidence on the current response page.</param>
        /// <returns>A copy of this evidence with the rank assigned.</returns>
        public SemanticSearchEvidence WithRank(int rank)
        {
            return new SemanticSearchEvidence(
                Text,
                ChunkOrdinal,
                Score,
                SearchParameterCanonical,
                SourceReference,
                SourcePath,
                rank);
        }
    }
}
