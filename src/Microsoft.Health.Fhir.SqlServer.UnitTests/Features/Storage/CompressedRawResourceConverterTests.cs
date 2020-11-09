// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Storage
{
    public class CompressedRawResourceConverterTests
    {
        [Fact]
        public async Task ResourceWithCurrentEncoding_WhenDecoded_ProducesCorrectResult()
        {
            string data = "Hello 😊";

            using var stream = new MemoryStream();
            CompressedRawResourceConverter.WriteCompressedRawResource(stream, data);

            stream.Seek(0, 0);
            string actual = await CompressedRawResourceConverter.ReadCompressedRawResource(stream);
            Assert.Equal(data, actual);
        }

        [Fact]
        public async Task ResourceWithLegacyEncoding_WhenDecoded_ProducesCorrectResult()
        {
            string data = "Hello 😊";

            using var stream = new MemoryStream();
            using var gzipStream = new GZipStream(stream, CompressionMode.Compress);
            using var writer = new StreamWriter(gzipStream, CompressedRawResourceConverter.LegacyResourceEncoding);

            writer.Write(data);
            writer.Flush();

            stream.Seek(0, 0);
            string actual = await CompressedRawResourceConverter.ReadCompressedRawResource(stream);
            Assert.Equal(data, actual);
        }
    }
}
