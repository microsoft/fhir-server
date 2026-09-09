// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch
{
    /// <summary>
    /// Extracts text from Binary content for vector indexing.
    /// </summary>
    public interface IBinaryContentExtractor
    {
        /// <summary>
        /// Gets the normalized MIME types supported by this extractor.
        /// </summary>
        IReadOnlyCollection<string> SupportedContentTypes { get; }

        /// <summary>
        /// Gets the maximum decoded Binary content length accepted by this extractor.
        /// </summary>
        /// <param name="maximumTextLength">The maximum extracted text length.</param>
        /// <returns>The maximum decoded content length in bytes.</returns>
        int GetMaximumContentLength(int maximumTextLength);

        /// <summary>
        /// Extracts text from decoded Binary data.
        /// </summary>
        /// <param name="content">The decoded Binary data.</param>
        /// <param name="contentType">The original Binary content type.</param>
        /// <param name="maximumTextLength">The maximum extracted text length.</param>
        /// <param name="segments">The ordered extracted text segments when successful.</param>
        /// <returns><see langword="true"/> when one or more non-empty text segments were extracted; otherwise <see langword="false"/>.</returns>
        bool TryExtract(byte[] content, string contentType, int maximumTextLength, out IReadOnlyList<BinaryContentSegment> segments);
    }
}
