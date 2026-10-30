// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch
{
    /// <summary>
    /// Splits document text into overlapping passages so each passage can be embedded on its own.
    /// </summary>
    public interface ITextChunker
    {
        /// <summary>
        /// Splits <paramref name="text"/> into ordered, overlapping passages.
        /// </summary>
        /// <param name="text">The text to split. Must not be null.</param>
        /// <param name="maxInputTokens">The maximum number of tokens retained from the source text.</param>
        /// <param name="chunkSizeTokens">The maximum number of tokens in each passage.</param>
        /// <param name="chunkOverlapTokens">The number of trailing tokens each passage shares with the next.</param>
        /// <returns>The ordered passages, or an empty list when <paramref name="text"/> is empty.</returns>
        IReadOnlyList<string> Chunk(string text, int maxInputTokens, int chunkSizeTokens, int chunkOverlapTokens);

        /// <summary>
        /// Counts tokens using the configured embedding model's tokenizer.
        /// </summary>
        /// <param name="text">The text whose tokens should be counted.</param>
        /// <returns>The number of model tokens in <paramref name="text"/>.</returns>
        int CountTokens(string text);
    }
}
