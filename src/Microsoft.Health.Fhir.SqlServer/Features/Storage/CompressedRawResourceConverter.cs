// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.Health.Fhir.Core.Features.Operations;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    /// <summary>
    /// Handles converting raw resource strings to compressed streams for storage in the database and vice-versa.
    /// </summary>
    internal class CompressedRawResourceConverter : ICompressedRawResourceConverter
    {
        internal static readonly Encoding LegacyResourceEncoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: false);
        internal static readonly Encoding ResourceEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

        public CompressedRawResourceConverter()
        {
        }

        public string ReadCompressedRawResource(Stream compressedResourceStream)
        {
            using var gzipStream = new GZipStream(compressedResourceStream, CompressionMode.Decompress, leaveOpen: true);

            // The current resource encoding uses byte-order marks. The legacy encoding does not, so we provide is as the fallback encoding
            // when there is no BOM
            using var reader = new StreamReader(gzipStream, LegacyResourceEncoding, detectEncodingFromByteOrderMarks: true);

            // The synchronous method is being used as it was found to be ~10x faster than the asynchronous method.
            return reader.ReadToEnd();
        }

        public void WriteCompressedRawResource(Stream outputStream, string rawResource)
        {
            using var gzipStream = new GZipStream(outputStream, CompressionMode.Compress, leaveOpen: true);
            using var writer = new StreamWriter(gzipStream, ResourceEncoding);
            writer.Write(rawResource);
        }
    }
}
