// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO;
using System.IO.Compression;
using System.Text;

namespace Microsoft.Health.Fhir.Store.Export
{
    internal class CompressedRawResourceConverterCopy
    {
        internal static readonly Encoding LegacyResourceEncoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: false);
        internal static readonly Encoding ResourceEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

        public CompressedRawResourceConverterCopy()
        {
        }

        public static string ReadCompressedRawResource(Stream compressedResourceStream)
        {
            using var gzipStream = new GZipStream(compressedResourceStream, CompressionMode.Decompress, leaveOpen: true);
            using var reader = new StreamReader(gzipStream, LegacyResourceEncoding, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEndAsync().Result;
        }

        public static void WriteCompressedRawResource(Stream outputStream, string rawResource)
        {
            using var gzipStream = new GZipStream(outputStream, CompressionMode.Compress, leaveOpen: true);
            using var writer = new StreamWriter(gzipStream, ResourceEncoding);
            writer.Write(rawResource);
        }
    }
}
