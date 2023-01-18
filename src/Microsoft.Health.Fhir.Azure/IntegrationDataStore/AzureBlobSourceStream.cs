// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Extensions.Logging;
using Microsoft.IO;
using Polly;

namespace Microsoft.Health.Fhir.Azure.IntegrationDataStore
{
    public class AzureBlobSourceStream : Stream
    {
        private const int BlobSizeThresholdWarningInBytes = 1000000; // 1MB threshold.
        private const int DefaultConcurrentCount = 3;
        public const int DefaultBlockBufferSize = 8 * 1024 * 1024;

        private Func<Task<ICloudBlob>> _blobClientFactory;
        private long _startOffset;
        private long _position;
        private ILogger _logger;
        private RecyclableMemoryStreamManager _recyclableMemoryStreamManager;

        private ICloudBlob _blobClient;
        private readonly Queue<Task<Stream>> _downloadTasks = new Queue<Task<Stream>>();

        public AzureBlobSourceStream(Func<Task<ICloudBlob>> blobClientFactory, long? startOffset, ILogger logger)
        {
            EnsureArg.IsNotNull(blobClientFactory, nameof(blobClientFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _blobClientFactory = blobClientFactory;
            _startOffset = startOffset ?? 0;
            _position = _startOffset;
            _logger = logger;

            _recyclableMemoryStreamManager = new RecyclableMemoryStreamManager();
        }

        public int ConcurrentCount
        {
            get;
            set;
        }

        = DefaultConcurrentCount;

        public int BlockBufferSize
        {
            get;
            set;
        }

        = DefaultBlockBufferSize;

        public override bool CanRead => true;

        public override long Position
        {
            get
            {
                return _position;
            }

            set => throw new NotImplementedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int totalBytesRead = 0;
            InitializeBlobClient();

            while (count > 0)
            {
                TryStartNewDownloadTask();

                if (_downloadTasks.Count > 0)
                {
                    Task<Stream> downloadTask = _downloadTasks.Peek();
                    downloadTask.Wait();

                    Stream contentStream = downloadTask.Result;
                    int bytesRead = contentStream.Read(buffer, offset, count);
                    if (bytesRead == 0)
                    {
                        contentStream.Dispose();
                        _downloadTasks.Dequeue().Dispose();
                        continue;
                    }

                    totalBytesRead += bytesRead;
                    offset += bytesRead;
                    count -= bytesRead;
                }
                else
                {
                    break;
                }
            }

            _position += offset;
            return totalBytesRead;
        }

        private int TryStartNewDownloadTask()
        {
            int newTasksStarted = 0;
            while (_downloadTasks.Count < ConcurrentCount)
            {
                (long position, long? length) nextRange = NextRange();

                // the range is empty => all data downloaded.
                if ((nextRange.length ?? 0) == 0)
                {
                    break;
                }

                _downloadTasks.Enqueue(DownloadBlobAsync(nextRange.position, nextRange.length ?? 0));
                newTasksStarted++;
            }

            return newTasksStarted;
        }

        private async Task<Stream> DownloadBlobAsync(long offset, long length)
        {
            return await Policy.Handle<StorageException>()
                    .WaitAndRetryAsync(
                        retryCount: 3,
                        sleepDurationProvider: (retryCount) => TimeSpan.FromSeconds(5 * (retryCount - 1)),
                        onRetryAsync: async (exception, retryCount) =>
                        {
                            _logger.LogWarning(exception, "Error while download blobs.");

                            await RefreshBlobClientAsync();
                        })
                    .ExecuteAsync(() => DownloadDataFunc(offset, length));
        }

        private (long offset, long? length) NextRange()
        {
            long totalLength = _blobClient.Properties.Length;
            long? length = null;
            long nextPosition = _startOffset;
            if (totalLength > _startOffset)
            {
                length = Math.Min(totalLength - _startOffset, BlockBufferSize);
            }

            _startOffset += length ?? 0;
            return (nextPosition, length);
        }

        private void InitializeBlobClient()
        {
            if (_blobClient == null)
            {
                _blobClient = _blobClientFactory().Result;
            }
        }

        private async Task RefreshBlobClientAsync()
        {
            _blobClient = await _blobClientFactory();
        }

        private async Task<Stream> DownloadDataFunc(long offset, long length)
        {
            // Stream is returned to the method caller, unable to dispose it under the current scope.
            var stream = new RecyclableMemoryStream(_recyclableMemoryStreamManager, tag: nameof(AzureBlobSourceStream));
            await _blobClient.DownloadRangeToStreamAsync(stream, offset, length);
            stream.Position = 0;

            if (stream.Length >= BlobSizeThresholdWarningInBytes)
            {
                _logger.LogInformation(
                    "{Origin} - MemoryWatch - Heavy blob downloaded. Blob size: {BlobSize}. Current memory in use: {MemoryInUse}.",
                    nameof(AzureBlobSourceStream),
                    stream.Length,
                    GC.GetTotalMemory(forceFullCollection: false));
            }

            return stream;
        }

#pragma warning disable SA1201 // Elements should appear in the correct order
        public override bool CanSeek => false;
#pragma warning restore SA1201 // Elements should appear in the correct order

        public override bool CanWrite => false;

#pragma warning disable CA1065 // Do not raise exceptions in unexpected locations
        public override long Length => throw new NotImplementedException();
#pragma warning restore CA1065 // Do not raise exceptions in unexpected locations

        public override void Flush()
        {
            throw new NotImplementedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }
    }
}
