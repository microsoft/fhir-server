// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO;
using System.IO.Compression;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Storage
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class CompressedRawResourceConverterTests
    {
        [Fact]
        public void ResourceWithCurrentEncoding_WhenDecoded_ProducesCorrectResult()
        {
            string data = "Hello 😊";

            CompressedRawResourceConverter converter = new CompressedRawResourceConverter();
            using var stream = new MemoryStream();
            converter.WriteCompressedRawResource(stream, data);

            stream.Seek(0, 0);
            string actual = converter.ReadCompressedRawResource(stream);
            Assert.Equal(data, actual);
        }

        [Fact]
        public void ResourceWithLegacyEncoding_WhenDecoded_ProducesCorrectResult()
        {
            string data = "Hello 😊";

            CompressedRawResourceConverter converter = new CompressedRawResourceConverter();
            using var stream = new MemoryStream();
            using var gzipStream = new GZipStream(stream, CompressionMode.Compress);
            using var writer = new StreamWriter(gzipStream, CompressedRawResourceConverter.LegacyResourceEncoding);

            writer.Write(data);
            writer.Flush();

            stream.Seek(0, 0);
            string actual = converter.ReadCompressedRawResource(stream);
            Assert.Equal(data, actual);
        }
    }
}
