// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.ML.Tokenizers;

namespace Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch
{
    /// <summary>
    /// Splits text into fixed-size, overlapping passages using the configured embedding model's tokenizer. The overlap
    /// keeps a clinical statement that lands on a boundary from being split across two passages.
    /// </summary>
    public sealed class TextChunker : ITextChunker
    {
        private readonly Tokenizer _tokenizer;

        /// <summary>
        /// Initializes a new instance of the <see cref="TextChunker"/> class.
        /// </summary>
        /// <param name="configuration">The vector search configuration containing the embedding model name.</param>
        public TextChunker(IOptions<VectorSearchConfiguration> configuration)
        {
            VectorSearchConfiguration vectorSearchConfiguration = EnsureArg.IsNotNull(configuration, nameof(configuration)).Value;
            string modelName = EnsureArg.IsNotNullOrWhiteSpace(vectorSearchConfiguration.Embedding?.ModelName, nameof(configuration));
            _tokenizer = TiktokenTokenizer.CreateForModel(modelName);
        }

        /// <inheritdoc />
        public IReadOnlyList<string> Chunk(string text, int maxInputTokens, int chunkSizeTokens, int chunkOverlapTokens)
        {
            EnsureArg.IsNotNull(text, nameof(text));
            EnsureArg.IsGt(maxInputTokens, 0, nameof(maxInputTokens));
            EnsureArg.IsGt(chunkSizeTokens, 0, nameof(chunkSizeTokens));
            EnsureArg.IsGte(chunkOverlapTokens, 0, nameof(chunkOverlapTokens));
            EnsureArg.IsLt(chunkOverlapTokens, chunkSizeTokens, nameof(chunkOverlapTokens));

            if (text.Length == 0)
            {
                return Array.Empty<string>();
            }

            string cappedText = GetPrefix(text, maxInputTokens);
            var chunks = new List<string>();
            int start = 0;

            while (start < cappedText.Length)
            {
                string remaining = cappedText.Substring(start);
                int relativeEnd = _tokenizer.GetIndexByTokenCount(remaining, chunkSizeTokens, out string normalizedText, out _);
                string processedText = normalizedText ?? remaining;
                relativeEnd = MoveBeforeSplitSurrogate(processedText, relativeEnd);
                if (relativeEnd == 0)
                {
                    throw new InvalidOperationException("The configured tokenizer could not fit any text within the chunk token limit.");
                }

                string chunk = processedText.Substring(0, relativeEnd);
                chunks.Add(chunk);

                if (relativeEnd == processedText.Length)
                {
                    break;
                }

                int overlapStart = chunk.Length;
                if (chunkOverlapTokens > 0)
                {
                    overlapStart = _tokenizer.GetIndexByTokenCountFromEnd(chunk, chunkOverlapTokens, out string normalizedChunk, out _);
                    string processedChunk = normalizedChunk ?? chunk;
                    overlapStart = MoveBeforeSplitSurrogate(processedChunk, overlapStart);
                }

                int nextStart = start + overlapStart;
                if (nextStart <= start)
                {
                    throw new InvalidOperationException("The configured token overlap did not advance the chunk window.");
                }

                start = nextStart;
            }

            return chunks;
        }

        /// <inheritdoc />
        public int CountTokens(string text)
        {
            EnsureArg.IsNotNull(text, nameof(text));
            return _tokenizer.CountTokens(text);
        }

        private string GetPrefix(string text, int maxInputTokens)
        {
            int end = _tokenizer.GetIndexByTokenCount(text, maxInputTokens, out string normalizedText, out _);
            string processedText = normalizedText ?? text;
            end = MoveBeforeSplitSurrogate(processedText, end);
            return processedText.Substring(0, end);
        }

        private static int MoveBeforeSplitSurrogate(string text, int index)
        {
            return index > 0 &&
                index < text.Length &&
                char.IsHighSurrogate(text[index - 1]) &&
                char.IsLowSurrogate(text[index])
                ? index - 1
                : index;
        }
    }
}
