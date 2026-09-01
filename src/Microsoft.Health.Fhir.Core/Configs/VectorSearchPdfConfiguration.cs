// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Configs
{
    /// <summary>
    /// Configures resource limits for extracting text from PDF Binary content.
    /// </summary>
    public sealed class VectorSearchPdfConfiguration
    {
        /// <summary>
        /// Gets or sets the maximum decoded PDF size in bytes.
        /// </summary>
        public int MaximumFileSizeBytes { get; set; } = 10 * 1024 * 1024;

        /// <summary>
        /// Gets or sets the maximum number of pages allowed in a PDF.
        /// </summary>
        public int MaximumPageCount { get; set; } = 200;

        /// <summary>
        /// Gets or sets the maximum total number of characters extracted from a PDF.
        /// </summary>
        public int MaximumExtractedCharacters { get; set; } = 500_000;

        /// <summary>
        /// Gets or sets the maximum elapsed time allowed for PDF text extraction.
        /// </summary>
        public TimeSpan ExtractionTimeout { get; set; } = TimeSpan.FromSeconds(30);
    }
}
