// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch
{
    /// <summary>
    /// Represents text extracted from one addressable segment of Binary content.
    /// </summary>
    public sealed class BinaryContentSegment
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BinaryContentSegment"/> class.
        /// </summary>
        /// <param name="text">The extracted text.</param>
        /// <param name="sourceLocator">The optional locator within the Binary data, such as a page number.</param>
        public BinaryContentSegment(string text, string sourceLocator = null)
        {
            Text = EnsureArg.IsNotNull(text, nameof(text));
            SourceLocator = sourceLocator;
        }

        /// <summary>
        /// Gets the extracted text.
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// Gets the optional locator within the Binary data.
        /// </summary>
        public string SourceLocator { get; }
    }
}
