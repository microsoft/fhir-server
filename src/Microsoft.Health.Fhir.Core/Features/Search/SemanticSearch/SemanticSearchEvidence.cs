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
        {
            EnsureArg.IsNotNullOrWhiteSpace(text, nameof(text));
            EnsureArg.IsGte(chunkOrdinal, 0, nameof(chunkOrdinal));
            EnsureArg.IsNotNull(searchParameterCanonical, nameof(searchParameterCanonical));
            EnsureArg.IsNotNullOrWhiteSpace(sourceReference, nameof(sourceReference));
            EnsureArg.IsNotNullOrWhiteSpace(sourcePath, nameof(sourcePath));

            if (!searchParameterCanonical.IsAbsoluteUri)
            {
                throw new ArgumentException("The SearchParameter canonical URL must be absolute.", nameof(searchParameterCanonical));
            }

            Text = text;
            ChunkOrdinal = chunkOrdinal;
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
    }
}
