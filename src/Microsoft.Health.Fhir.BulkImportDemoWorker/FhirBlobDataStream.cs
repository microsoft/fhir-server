// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Microsoft.Health.Fhir.BulkImportDemoWorker
{
    public class FhirBlobDataStream : Stream
    {
        private BlobClient _blobClient;
        private Lazy<long> _blobLength;
        private Queue<Task<Stream>> _downloadTasks;
        private long _position;

        public FhirBlobDataStream(Uri blobUri, TokenCredential credential)
        {
            BlobClientOptions options = new BlobClientOptions();
            _blobClient = new BlobClient(blobUri, credential, options);
            _blobLength = new Lazy<long>(() => _blobClient.GetProperties().Value.ContentLength);
            _downloadTasks = new Queue<Task<Stream>>();
            _position = 0;
        }

        public int ConcurrentCount
        {
            get;
            set;
        }

        = BulkImportConstants.DefaultConcurrentCount;

        public int BlockDownloadTimeoutInSeconds
        {
            get;
            set;
        }

        = BulkImportConstants.DefaultBlockDownloadTimeoutInSeconds;

        public int RetryDelayInSecconds
        {
            get;
            set;
        }

        = BulkImportConstants.RetryDelayInSecconds;

        public int BlockDownloadTimeoutRetryCount
        {
            get;
            set;
        }

        = BulkImportConstants.DefaultBlockDownloadTimeoutRetryCount;

        public override bool CanRead => true;

        public static async Task<Stream> DownloadDataFunc(BlobClient client, HttpRange range)
        {
            using BlobDownloadInfo blobDownloadInfo = await client.DownloadAsync(range).ConfigureAwait(false);
            MemoryStream stream = new MemoryStream();
            await blobDownloadInfo.Content.CopyToAsync(stream).ConfigureAwait(false);
            stream.Position = 0;
            return stream;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int totalBytesRead = 0;

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

            return totalBytesRead;
        }

        private int TryStartNewDownloadTask()
        {
            int newTasksStarted = 0;
            while (_downloadTasks.Count < ConcurrentCount)
            {
                HttpRange nextRange = NextRange();

                // the range is empty => all data downloaded.
                if ((nextRange.Length ?? 0) == 0)
                {
                    break;
                }

                _downloadTasks.Enqueue(DownloadBlobAsync(nextRange));
                _position += nextRange.Length ?? 0;
                newTasksStarted++;
            }

            return newTasksStarted;
        }

        private async Task<Stream> DownloadBlobAsync(HttpRange range)
        {
            return await OperationExecutionHelper.InvokeWithTimeoutRetryAsync<Stream>(
                async () =>
                {
                    return await DownloadDataFunc(_blobClient, range).ConfigureAwait(false);
                },
                timeout: TimeSpan.FromSeconds(BlockDownloadTimeoutInSeconds),
                BlockDownloadTimeoutRetryCount,
                delayInSec: RetryDelayInSecconds,
                isRetrableException: OperationExecutionHelper.IsRetrableException).ConfigureAwait(false);
        }

        private HttpRange NextRange()
        {
            long totalLength = _blobLength.Value;
            long? length = null;
            if (totalLength > _position)
            {
                length = Math.Min(totalLength - _position, BulkImportConstants.BlockBufferSize);
            }

            return new HttpRange(_position, length);
        }

#pragma warning disable SA1201 // Elements should appear in the correct order
        public override bool CanSeek => false;
#pragma warning restore SA1201 // Elements should appear in the correct order

        public override bool CanWrite => false;

#pragma warning disable CA1065 // Do not raise exceptions in unexpected locations
        public override long Length => throw new NotImplementedException();
#pragma warning restore CA1065 // Do not raise exceptions in unexpected locations

#pragma warning disable CA1065 // Do not raise exceptions in unexpected locations
        public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
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
