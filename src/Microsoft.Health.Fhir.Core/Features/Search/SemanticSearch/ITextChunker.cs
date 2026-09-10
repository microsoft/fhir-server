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
        /// <param name="chunkSize">The maximum length of each passage. Must be greater than zero.</param>
        /// <param name="chunkOverlap">The number of trailing characters each passage shares with the next. Must be at least zero and less than <paramref name="chunkSize"/>.</param>
        /// <returns>The ordered passages, or an empty list when <paramref name="text"/> is empty.</returns>
        IReadOnlyList<string> Chunk(string text, int chunkSize, int chunkOverlap);
    }
}
