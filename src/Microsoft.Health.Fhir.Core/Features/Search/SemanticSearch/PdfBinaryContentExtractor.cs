// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using EnsureThat;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Exceptions;
using UglyToad.PdfPig.Fonts;

namespace Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch
{
    /// <summary>
    /// Extracts page-scoped text from PDF Binary content.
    /// </summary>
    public sealed class PdfBinaryContentExtractor : IBinaryContentExtractor
    {
        private readonly VectorSearchPdfConfiguration _configuration;

        /// <summary>
        /// Initializes a new instance of the <see cref="PdfBinaryContentExtractor"/> class.
        /// </summary>
        /// <param name="configuration">The vector search configuration.</param>
        public PdfBinaryContentExtractor(IOptions<VectorSearchConfiguration> configuration)
        {
            _configuration = EnsureArg.IsNotNull(configuration, nameof(configuration)).Value.Indexing.Pdf;
        }

        /// <inheritdoc />
        public IReadOnlyCollection<string> SupportedContentTypes { get; } = new[] { "application/pdf" };

        /// <inheritdoc />
        public int GetMaximumContentLength(int maximumTextLength)
        {
            return _configuration.MaximumFileSizeBytes;
        }

        /// <inheritdoc />
        public bool TryExtract(byte[] content, string contentType, int maximumTextLength, out IReadOnlyList<BinaryContentSegment> segments)
        {
            EnsureArg.IsNotNull(content, nameof(content));
            EnsureArg.IsNotNullOrWhiteSpace(contentType, nameof(contentType));
            EnsureArg.IsGt(maximumTextLength, 0, nameof(maximumTextLength));

            segments = null;
            if (content.Length == 0 || content.Length > _configuration.MaximumFileSizeBytes)
            {
                return false;
            }

            try
            {
                var stopwatch = Stopwatch.StartNew();
                using PdfDocument document = PdfDocument.Open(content);
                if (stopwatch.Elapsed > _configuration.ExtractionTimeout)
                {
                    return false;
                }

                if (document.NumberOfPages <= 0 || document.NumberOfPages > _configuration.MaximumPageCount)
                {
                    return false;
                }

                var extractedSegments = new List<BinaryContentSegment>();
                int maximumCharacters = Math.Min(maximumTextLength, _configuration.MaximumExtractedCharacters);
                int extractedCharacterCount = 0;

                for (int pageNumber = 1; pageNumber <= document.NumberOfPages; pageNumber++)
                {
                    if (stopwatch.Elapsed > _configuration.ExtractionTimeout)
                    {
                        return false;
                    }

                    Page page = document.GetPage(pageNumber);
                    string text = ContentOrderTextExtractor.GetText(page);
                    if (stopwatch.Elapsed > _configuration.ExtractionTimeout)
                    {
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    if (text.Length > maximumCharacters - extractedCharacterCount)
                    {
                        return false;
                    }

                    extractedCharacterCount += text.Length;
                    extractedSegments.Add(new BinaryContentSegment(text, $"page={pageNumber}"));
                }

                if (extractedSegments.Count == 0)
                {
                    return false;
                }

                segments = extractedSegments;
                return true;
            }
            catch (Exception exception) when (
                exception is PdfDocumentFormatException or
                PdfDocumentStackDepthException or
                PdfDocumentEncryptedException or
                CorruptCompressedDataException or
                InvalidFontFormatException)
            {
                return false;
            }
        }
    }
}
