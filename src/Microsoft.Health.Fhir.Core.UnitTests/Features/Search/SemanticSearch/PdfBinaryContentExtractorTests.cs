// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.SemanticSearch
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public sealed class PdfBinaryContentExtractorTests
    {
        [Fact]
        public void GivenTextPdf_WhenExtracting_ThenPageSegmentsAreReturnedInOrder()
        {
            // Arrange
            PdfBinaryContentExtractor extractor = CreateExtractor();
            byte[] content = CreatePdf("first clinical page", "second clinical page");

            // Act
            bool extracted = extractor.TryExtract(content, "application/pdf", 1000, out IReadOnlyList<BinaryContentSegment> segments);

            // Assert
            Assert.True(extracted);
            Assert.Collection(
                segments,
                segment =>
                {
                    Assert.Contains("first clinical page", segment.Text, StringComparison.Ordinal);
                    Assert.Equal("page=1", segment.SourceLocator);
                },
                segment =>
                {
                    Assert.Contains("second clinical page", segment.Text, StringComparison.Ordinal);
                    Assert.Equal("page=2", segment.SourceLocator);
                });
        }

        [Fact]
        public void GivenPdfWithoutText_WhenExtracting_ThenExtractionFails()
        {
            // Arrange
            PdfBinaryContentExtractor extractor = CreateExtractor();
            byte[] content = CreatePdf(new string[] { null });

            // Act
            bool extracted = extractor.TryExtract(content, "application/pdf", 1000, out IReadOnlyList<BinaryContentSegment> segments);

            // Assert
            Assert.False(extracted);
            Assert.Null(segments);
        }

        [Fact]
        public void GivenMalformedPdf_WhenExtracting_ThenExtractionFails()
        {
            // Arrange
            PdfBinaryContentExtractor extractor = CreateExtractor();

            // Act
            bool extracted = extractor.TryExtract(Encoding.UTF8.GetBytes("not a PDF"), "application/pdf", 1000, out IReadOnlyList<BinaryContentSegment> segments);

            // Assert
            Assert.False(extracted);
            Assert.Null(segments);
        }

        [Fact]
        public void GivenPdfExceedingPageLimit_WhenExtracting_ThenExtractionFails()
        {
            // Arrange
            var configuration = new VectorSearchConfiguration();
            configuration.Indexing.Pdf.MaximumPageCount = 1;
            PdfBinaryContentExtractor extractor = CreateExtractor(configuration);
            byte[] content = CreatePdf("first page", "second page");

            // Act
            bool extracted = extractor.TryExtract(content, "application/pdf", 1000, out IReadOnlyList<BinaryContentSegment> segments);

            // Assert
            Assert.False(extracted);
            Assert.Null(segments);
        }

        [Fact]
        public void GivenPdfExceedingCharacterLimit_WhenExtracting_ThenExtractionFails()
        {
            // Arrange
            var configuration = new VectorSearchConfiguration();
            configuration.Indexing.Pdf.MaximumExtractedCharacters = 5;
            PdfBinaryContentExtractor extractor = CreateExtractor(configuration);
            byte[] content = CreatePdf("clinical text");

            // Act
            bool extracted = extractor.TryExtract(content, "application/pdf", 1000, out IReadOnlyList<BinaryContentSegment> segments);

            // Assert
            Assert.False(extracted);
            Assert.Null(segments);
        }

        [Fact]
        public void GivenPdfExceedingFileLimit_WhenExtracting_ThenExtractionFails()
        {
            // Arrange
            byte[] content = CreatePdf("clinical text");
            var configuration = new VectorSearchConfiguration();
            configuration.Indexing.Pdf.MaximumFileSizeBytes = content.Length - 1;
            PdfBinaryContentExtractor extractor = CreateExtractor(configuration);

            // Act
            bool extracted = extractor.TryExtract(content, "application/pdf", 1000, out IReadOnlyList<BinaryContentSegment> segments);

            // Assert
            Assert.False(extracted);
            Assert.Null(segments);
            Assert.Equal(content.Length - 1, extractor.GetMaximumContentLength(maximumTextLength: 1));
        }

        private static PdfBinaryContentExtractor CreateExtractor(VectorSearchConfiguration configuration = null)
        {
            return new PdfBinaryContentExtractor(Options.Create(configuration ?? new VectorSearchConfiguration()));
        }

        private static byte[] CreatePdf(params string[] pageTexts)
        {
            var builder = new PdfDocumentBuilder();
            PdfDocumentBuilder.AddedFont font = builder.AddStandard14Font(Standard14Font.Helvetica);

            foreach (string pageText in pageTexts)
            {
                PdfPageBuilder page = builder.AddPage(PageSize.A4);
                if (pageText != null)
                {
                    page.AddText(pageText, 12, new PdfPoint(25, 700), font);
                }
            }

            return builder.Build();
        }
    }
}
