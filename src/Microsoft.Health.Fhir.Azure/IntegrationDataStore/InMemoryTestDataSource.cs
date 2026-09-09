// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
using Microsoft.Health.Fhir.Core.Features.Operations;

namespace Microsoft.Health.Fhir.Azure.IntegrationDataStore
{
    /// <summary>
    /// Provides an in-memory, synthetic NDJSON payload that <see cref="AzureBlobIntegrationDataStoreClient"/>
    /// can serve in place of a real Azure Storage blob, for measuring the CPU cost of the $import processing
    /// pipeline without incurring real storage or database I/O.
    /// </summary>
    /// <remarks>
    /// This source only activates for resource URIs using the reserved
    /// <see cref="IntegrationDataStoreClientConstants.InMemoryTestSourceScheme"/> scheme, which is never used
    /// for real storage endpoints.
    /// </remarks>
    internal static class InMemoryTestDataSource
    {
        private const string EmbeddedResourceName = "Microsoft.Health.Fhir.Azure.IntegrationDataStore.TestData.representative-import-1000.ndjson.gz";

        private static readonly Lazy<byte[]> LazyPayload = new Lazy<byte[]>(LoadPayload, LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>
        /// Determines whether the given resource URI refers to the in-memory test source.
        /// Any URI with the <see cref="IntegrationDataStoreClientConstants.InMemoryTestSourceScheme"/> scheme
        /// (e.g., "inmemorytest://representative-import-1000", "inmemorytest://batch-1", etc.) is accepted
        /// and serves the same embedded payload. This allows tests to use multiple distinct URLs without
        /// requiring separate real storage blobs.
        /// </summary>
        internal static bool IsTestSourceUri(Uri resourceUri)
        {
            return resourceUri != null && string.Equals(resourceUri.Scheme, IntegrationDataStoreClientConstants.InMemoryTestSourceScheme, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns a read-only stream over the decompressed sample payload, positioned at <paramref name="startOffset"/>.
        /// </summary>
        internal static Stream GetStream(long startOffset)
        {
            byte[] payload = LazyPayload.Value;
            var stream = new MemoryStream(payload, writable: false);
            stream.Seek(startOffset, SeekOrigin.Begin);
            return stream;
        }

        /// <summary>
        /// Returns fixed properties describing the in-memory payload, matching the shape returned by
        /// the real Azure Blob client for <see cref="IIntegrationDataStoreClient.GetPropertiesAsync"/>.
        /// </summary>
        internal static Dictionary<string, object> GetProperties()
        {
            byte[] payload = LazyPayload.Value;

            return new Dictionary<string, object>
            {
                [IntegrationDataStoreClientConstants.BlobPropertyETag] = "\"in-memory-test-source\"",
                [IntegrationDataStoreClientConstants.BlobPropertyLength] = (long)payload.Length,
            };
        }

        /// <summary>
        /// Returns a fixed, deterministic lease id for the in-memory test source. No real lease is taken since
        /// there is no shared resource to coordinate access to.
        /// </summary>
        internal static string AcquireLease()
        {
            return "in-memory-test-source-lease";
        }

        private static byte[] LoadPayload()
        {
            Assembly assembly = typeof(InMemoryTestDataSource).Assembly;

            using Stream compressedStream = assembly.GetManifestResourceStream(EmbeddedResourceName)
                ?? throw new InvalidOperationException($"Embedded resource '{EmbeddedResourceName}' was not found.");
            using var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress);
            using var decompressedStream = new MemoryStream();

            gzipStream.CopyTo(decompressedStream);

            return decompressedStream.ToArray();
        }
    }
}
