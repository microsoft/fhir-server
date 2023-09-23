// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.IO;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    public sealed class ContinuationTokenConverter
    {
        private static readonly RecyclableMemoryStreamManager StreamManager = new();
        private const string TokenVersion = "v2|";

        public static string Decode(string encodedContinuationToken)
        {
            try
            {
                byte[] continuationTokenBytes = Convert.FromBase64String(encodedContinuationToken);

                try
                {
                    using MemoryStream memoryStream = StreamManager.GetStream(nameof(ContinuationTokenConverter), continuationTokenBytes, 0, continuationTokenBytes.Length);
                    using var deflate = new DeflateStream(memoryStream, CompressionMode.Decompress);
                    using var reader = new StreamReader(deflate, Encoding.UTF8);

                    var token = reader.ReadToEnd();
                    if (token?.StartsWith(TokenVersion, StringComparison.Ordinal) == true)
                    {
                        return token.Substring(TokenVersion.Length);
                    }

                    return Encoding.UTF8.GetString(continuationTokenBytes);
                }
                catch (InvalidDataException)
                {
                    // Fall back to compatibility with non-compressed tokens
                    return Encoding.UTF8.GetString(continuationTokenBytes);
                }
            }
            catch (FormatException)
            {
                throw new BadRequestException(Core.Resources.InvalidContinuationToken);
            }
        }

        public static string Encode(string continuationToken)
        {
            EnsureArg.IsNotEmptyOrWhiteSpace(continuationToken);

            if (int.TryParse(continuationToken, out _))
            {
                return continuationToken;
            }

            using MemoryStream memoryStream = StreamManager.GetStream(tag: nameof(ContinuationTokenConverter));
            using var deflate = new DeflateStream(memoryStream, CompressionLevel.Fastest);
            using var writer = new StreamWriter(deflate, Encoding.UTF8);

            writer.Write(TokenVersion);
            writer.Write(continuationToken);
            writer.Flush();

            return Convert.ToBase64String(memoryStream.ToArray());
        }
    }
}
