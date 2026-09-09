// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch
{
    /// <summary>
    /// Splits text into fixed-size, overlapping passages using a sliding character window. The overlap
    /// keeps a clinical statement that lands on a boundary from being split across two passages.
    /// </summary>
    public sealed class TextChunker : ITextChunker
    {
        /// <inheritdoc />
        public IReadOnlyList<string> Chunk(string text, int chunkSize, int chunkOverlap)
        {
            EnsureArg.IsNotNull(text, nameof(text));
            EnsureArg.IsGt(chunkSize, 0, nameof(chunkSize));
            EnsureArg.IsGte(chunkOverlap, 0, nameof(chunkOverlap));
            EnsureArg.IsLt(chunkOverlap, chunkSize, nameof(chunkOverlap));

            if (text.Length <= chunkSize)
            {
                return text.Length == 0 ? Array.Empty<string>() : new[] { text };
            }

            int step = chunkSize - chunkOverlap;
            var chunks = new List<string>();

            for (int start = 0; start < text.Length; start += step)
            {
                int length = Math.Min(chunkSize, text.Length - start);
                chunks.Add(text.Substring(start, length));

                if (start + length == text.Length)
                {
                    break;
                }
            }

            return chunks;
        }
    }
}
