// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.IO.Compression;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.IO;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    public sealed class ContinuationTokenConverter
    {
        private static readonly RecyclableMemoryStreamManager StreamManager = new();

        public static string Decode(string encodedContinuationToken)
        {
            try
            {
                byte[] continuationTokenBytes = Convert.FromBase64String(encodedContinuationToken);

                try
                {
                    using MemoryStream memoryStream = StreamManager.GetStream(continuationTokenBytes);
                    using var deflate = new DeflateStream(memoryStream, CompressionMode.Decompress);
                    using var reader = new StreamReader(deflate);
                    return reader.ReadToEnd();
                }
                catch (InvalidDataException)
                {
                    // Fall back to compatibility with non-compressed tokens
                    return System.Text.Encoding.UTF8.GetString(continuationTokenBytes);
                }
            }
            catch (FormatException)
            {
                throw new BadRequestException(Resources.InvalidContinuationToken);
            }
        }

        public static string Encode(string continuationToken)
        {
            EnsureArg.IsNotEmptyOrWhiteSpace(continuationToken);

            var buffer = System.Text.Encoding.UTF8.GetBytes(continuationToken);

            using MemoryStream memoryStream = StreamManager.GetStream();
            using var deflate = new DeflateStream(memoryStream, CompressionLevel.Fastest);
            deflate.Write(buffer);
            deflate.Flush();

            return Convert.ToBase64String(memoryStream.ToArray());
        }
    }
}
