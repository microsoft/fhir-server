// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch
{
    /// <summary>
    /// Extracts strict UTF-8 text from plain-text Binary content.
    /// </summary>
    public sealed class PlainTextBinaryContentExtractor : IBinaryContentExtractor
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        /// <inheritdoc />
        public IReadOnlyCollection<string> SupportedContentTypes { get; } = new[] { "text/plain" };

        /// <inheritdoc />
        public int GetMaximumContentLength(int maximumTextLength)
        {
            return maximumTextLength;
        }

        /// <inheritdoc />
        public bool TryExtract(byte[] content, string contentType, int maximumTextLength, out IReadOnlyList<BinaryContentSegment> segments)
        {
            EnsureArg.IsNotNull(content, nameof(content));
            EnsureArg.IsNotNullOrWhiteSpace(contentType, nameof(contentType));
            EnsureArg.IsGt(maximumTextLength, 0, nameof(maximumTextLength));

            segments = null;
            if (!IsUtf8(contentType))
            {
                return false;
            }

            try
            {
                string extractedText = StrictUtf8.GetString(content);
                if (extractedText.Length > maximumTextLength || string.IsNullOrWhiteSpace(extractedText))
                {
                    return false;
                }

                segments = new[] { new BinaryContentSegment(extractedText) };
                return true;
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
        }

        private static bool IsUtf8(string contentType)
        {
            string charset = contentType
                .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Skip(1)
                .FirstOrDefault(part => part.StartsWith("charset=", StringComparison.OrdinalIgnoreCase));

            return charset == null || string.Equals(charset.Substring("charset=".Length).Trim('"'), "utf-8", StringComparison.OrdinalIgnoreCase);
        }
    }
}
