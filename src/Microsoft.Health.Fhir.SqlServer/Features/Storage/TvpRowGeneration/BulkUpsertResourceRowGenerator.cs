// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Schema.Model;
using Microsoft.IO;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration
{
    internal class BulkUpsertResourceRowGenerator : ITableValuedParameterRowGenerator<IReadOnlyList<ResourceMetadata>, VLatest.BulkUpsertResourceTableTypeRow>
    {
        private readonly SqlServerFhirModel _model;
        private readonly RecyclableMemoryStreamManager _memoryStreamManager;

        public BulkUpsertResourceRowGenerator(SqlServerFhirModel model)
        {
            EnsureArg.IsNotNull(model, nameof(model));

            _model = model;
            _memoryStreamManager = new RecyclableMemoryStreamManager();
        }

        public IEnumerable<VLatest.BulkUpsertResourceTableTypeRow> GenerateRows(IReadOnlyList<ResourceMetadata> input)
        {
            for (var index = 0; index < input.Count; index++)
            {
                ResourceMetadata resourceMetadata = input[index];

                int etag = 0;
                if (resourceMetadata.WeakETag != null && !int.TryParse(resourceMetadata.WeakETag.VersionId, out etag))
                {
                    // Set the etag to a sentinel value to enable expected failure paths when updating with both existing and nonexistent resources.
                    etag = -1;
                }

                yield return new VLatest.BulkUpsertResourceTableTypeRow(
                    index,
                    _model.GetResourceTypeId(resourceMetadata.ResourceWrapper.ResourceTypeName),
                    resourceMetadata.ResourceWrapper.ResourceId,
                    resourceMetadata.WeakETag == null ? null : (int?)etag,
                    resourceMetadata.AllowCreate,
                    resourceMetadata.ResourceWrapper.IsDeleted,
                    resourceMetadata.KeepHistory,
                    resourceMetadata.ResourceWrapper.Request.Method,
                    new ResourceStream(_memoryStreamManager, resourceMetadata.ResourceWrapper.RawResource));
            }
        }

        private class ResourceStream : Stream
        {
            private readonly RecyclableMemoryStream _stream;

            public ResourceStream(RecyclableMemoryStreamManager memoryStreamManager, RawResource rawResource)
            {
                _stream = new RecyclableMemoryStream(memoryStreamManager);

                using (var gzipStream = new GZipStream(_stream, CompressionMode.Compress, true))
                using (var writer = new StreamWriter(gzipStream, SqlServerFhirDataStore.ResourceEncoding))
                {
                    writer.Write(rawResource.Data);
                    writer.Flush();
                }

                _stream.Seek(0, 0);
            }

            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => _stream.Length;

            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                int readCount = _stream.Read(buffer, offset, count);
                if (readCount == 0)
                {
                    Dispose();
                }

                return readCount;
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                _stream.Dispose();
            }
        }
    }
}
