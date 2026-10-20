// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;

namespace Microsoft.Health.Fhir.Azure.ExportDestinationClient
{
    public class AzureExportDestinationClient : IExportDestinationClient
    {
        private BlobServiceClient _blobClient = null;
        private BlobContainerClient _blobContainer = null;

        private readonly Dictionary<string, BlockBlobWriter> _blobWriters = new Dictionary<string, BlockBlobWriter>();
        private DestinationConnectionException _commitFailure;

        private readonly IExportClientInitializer<BlobServiceClient> _exportClientInitializer;
        private readonly ExportJobConfiguration _exportJobConfiguration;
        private readonly ILogger _logger;

        private const int RetryDelaySeconds = 3;

        public AzureExportDestinationClient(
            IExportClientInitializer<BlobServiceClient> exportClientInitializer,
            IOptions<ExportJobConfiguration> exportJobConfiguration,
            ILogger<AzureExportDestinationClient> logger)
        {
            EnsureArg.IsNotNull(exportClientInitializer, nameof(exportClientInitializer));
            EnsureArg.IsNotNull(exportJobConfiguration?.Value, nameof(exportJobConfiguration));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _exportClientInitializer = exportClientInitializer;
            _exportJobConfiguration = exportJobConfiguration.Value;
            _logger = logger;
        }

        public async Task ConnectAsync(CancellationToken cancellationToken, string containerId = null)
        {
            await ConnectAsync(_exportJobConfiguration, cancellationToken, containerId);
        }

        public async Task ConnectAsync(ExportJobConfiguration exportJobConfiguration, CancellationToken cancellationToken, string containerId = null)
        {
            _commitFailure = null;

            try
            {
                _blobClient = _exportClientInitializer.GetAuthorizedClient(exportJobConfiguration);
            }
            catch (ExportClientInitializerException ece)
            {
                _logger.LogError(ece, "Unable to initialize export client");

                throw new DestinationConnectionException(ece.Message, ece.StatusCode);
            }

            await CreateContainerAsync(_blobClient, containerId, cancellationToken);
        }

        private async Task CreateContainerAsync(BlobServiceClient blobClient, string containerId, CancellationToken cancellationToken)
        {
            _blobContainer = blobClient.GetBlobContainerClient(containerId);

            try
            {
                await _blobContainer.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            }
            catch (RequestFailedException se)
            {
                _logger.LogWarning(se, "{Error}", se.Message);

                throw new DestinationConnectionException(se.Message, (HttpStatusCode)se.Status);
            }
            catch (AggregateException ex) when (ex.InnerExceptions[0] is RequestFailedException)
            {
                // The blob container has added a 6 attempt retry that creates an aggregate exception if it can't find the blob.
                var innerException = (RequestFailedException)ex.InnerExceptions[0];

                // If storage account is not found
                if (ex.InnerExceptions[0].Message.Contains("No such host is known", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(ex, "The Storage account not found");
                    throw new DestinationConnectionException(Resources.StorageAccountNotFound, HttpStatusCode.NotFound);
                }
                else
                {
                    _logger.LogWarning(innerException, "{Error}", innerException.Message);
                    throw new DestinationConnectionException(innerException.Message, (HttpStatusCode)innerException.Status);
                }
            }
            catch (AccessTokenProviderException ex)
            {
                // Can't get an access token, likely an error with setup
                _logger.LogWarning(ex, "Failed to get access token for export");
                throw new DestinationConnectionException(Resources.CannotGetAccessToken, HttpStatusCode.Forbidden);
            }
            catch (ArgumentNullException ex) when (ex.Message.Contains("credentialBundleName", StringComparison.OrdinalIgnoreCase))
            {
                // This indicates that Managed Identity isn't setup
                _logger.LogWarning(ex, "Failed to get access token for export");
                throw new DestinationConnectionException(Resources.CannotGetAccessToken, HttpStatusCode.Forbidden);
            }
        }

        public void WriteFilePart(string fileName, string data)
        {
            EnsureArg.IsNotNull(fileName, nameof(fileName));
            EnsureArg.IsNotNull(data, nameof(data));
            CheckIfClientIsConnected();
            ThrowIfCommitFailed();

            if (!_blobWriters.TryGetValue(fileName, out BlockBlobWriter blobWriter))
            {
                blobWriter = new BlockBlobWriter(_blobContainer.GetBlockBlobClient(fileName));
                _blobWriters.Add(fileName, blobWriter);
            }

            blobWriter.WriteLine(data);
        }

        public IDictionary<string, Uri> Commit()
        {
            ThrowIfCommitFailed();

            Dictionary<string, Uri> blobUris = new Dictionary<string, Uri>();

            foreach (KeyValuePair<string, BlockBlobWriter> entry in _blobWriters)
            {
                blobUris.Add(entry.Key, CommitWriter(entry.Key, entry.Value));
            }

            ReleaseWriters();
            return blobUris;
        }

        public Uri CommitFile(string fileName)
        {
            ThrowIfCommitFailed();

            if (!_blobWriters.TryGetValue(fileName, out BlockBlobWriter blobWriter))
            {
                throw new ArgumentException($"Cannot commit non-existant file {fileName}");
            }

            Uri uri = CommitWriter(fileName, blobWriter);

            blobWriter.Dispose();
            _blobWriters.Remove(fileName);
            return uri;
        }

        private Uri CommitWriter(string fileName, BlockBlobWriter blobWriter)
        {
            try
            {
                return blobWriter.Commit();
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex, "Failed to write export file {FileName}", fileName);

                // Add a small delay before retrying in case the storage account is throttling.
                Task.Delay(TimeSpan.FromSeconds(RetryDelaySeconds)).Wait();

                try
                {
                    // Committing again is safe: it commits the same block list, so a retry can't duplicate data.
                    return blobWriter.Commit();
                }
                catch (RequestFailedException ex2)
                {
                    _logger.LogError(ex2, "Failed to write export file {FileName} on retry", fileName);

                    // Fail closed. The export job retry can't safely continue after a failed commit: on the final page it
                    // writes the page again, which would duplicate records in a kept writer, and on earlier pages it
                    // continues after data that only a kept writer holds, which a new writer would lose. Rejecting all
                    // further writes and commits makes the export fail instead of producing incorrect files.
                    var failure = new DestinationConnectionException(ex2.Message, (HttpStatusCode)ex2.Status);
                    _commitFailure = failure;
                    ReleaseWriters();
                    throw failure;
                }
            }
        }

        private void ReleaseWriters()
        {
            foreach (BlockBlobWriter blobWriter in _blobWriters.Values)
            {
                blobWriter.Dispose();
            }

            _blobWriters.Clear();
        }

        private void ThrowIfCommitFailed()
        {
            if (_commitFailure is not null)
            {
                throw new DestinationConnectionException(_commitFailure.Message, _commitFailure.StatusCode);
            }
        }

        private void CheckIfClientIsConnected()
        {
            if (_blobClient == null || _blobContainer == null)
            {
                throw new DestinationConnectionException(Resources.DestinationClientNotConnected, HttpStatusCode.InternalServerError);
            }
        }

        /// <summary>
        /// Writes the lines of one export file and uploads them as the blocks of a block blob.
        /// </summary>
        /// <remarks>
        /// This writer replaces <c>BlockBlobClient.OpenWrite</c>.
        /// OpenWrite commits the block list with an If-Match condition on the blob ETag. When storage applies a commit
        /// but the response is lost, for example while the account is throttling, the SDK retries the commit with the
        /// old ETag and storage rejects it with 412 ConditionNotMet, which fails the export.
        /// This writer commits without a condition. Each export file is written by exactly one export job, and
        /// committing the same block list again produces the same blob, so retries are safe and succeed.
        /// </remarks>
        private sealed class BlockBlobWriter : IDisposable
        {
            private readonly BlockStagingStream _stagingStream;
            private readonly StreamWriter _streamWriter;

            public BlockBlobWriter(BlockBlobClient blockBlob)
            {
                _stagingStream = new BlockStagingStream(blockBlob);

                // The small character buffer makes the writer encode and pass data to the staging stream in small chunks,
                // so a large resource is uploaded as it's written instead of being buffered whole.
                _streamWriter = new StreamWriter(_stagingStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 1024, leaveOpen: true);
            }

            /// <summary>
            /// Writes a line to the file. Full blocks are uploaded as they fill.
            /// </summary>
            /// <param name="data">The line to write.</param>
            public void WriteLine(string data)
            {
                _streamWriter.WriteLine(data);
            }

            /// <summary>
            /// Uploads any buffered data and commits all blocks, without an If-Match condition.
            /// </summary>
            /// <returns>The URI of the committed blob.</returns>
            public Uri Commit()
            {
                _streamWriter.Flush();
                return _stagingStream.Commit();
            }

            public void Dispose()
            {
                // Writers are disposed after a commit or when they're abandoned. Discard anything still buffered so disposal
                // never uploads data.
                _stagingStream.Discard();
                _streamWriter.Dispose();
                _stagingStream.Dispose();
            }
        }

        /// <summary>
        /// A write-only stream that uploads data as blocks of a block blob when about 4 MB is buffered.
        /// </summary>
        private sealed class BlockStagingStream : Stream
        {
            // Matches the default buffer size of OpenWrite, so memory use per open file is unchanged.
            private const int BlockSizeInBytes = 4 * 1024 * 1024;

            private readonly BlockBlobClient _blockBlob;
            private readonly MemoryStream _pending = new MemoryStream();
            private readonly List<string> _blockIds = new List<string>();
            private bool _discarding;

            public BlockStagingStream(BlockBlobClient blockBlob)
            {
                _blockBlob = blockBlob;
            }

            public override bool CanRead => false;

            public override bool CanSeek => false;

            public override bool CanWrite => true;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                Write(new ReadOnlySpan<byte>(buffer, offset, count));
            }

            public override void Write(ReadOnlySpan<byte> buffer)
            {
                if (_discarding)
                {
                    return;
                }

                // Buffer all bytes before uploading, so a failed upload loses nothing and a later attempt uploads everything.
                _pending.Write(buffer);

                if (_pending.Length >= BlockSizeInBytes)
                {
                    StagePendingData();
                }
            }

            /// <summary>
            /// Stops uploading. Later writes are ignored.
            /// </summary>
            public void Discard()
            {
                _discarding = true;
            }

            /// <summary>
            /// Uploads any pending data and commits all blocks, without an If-Match condition.
            /// </summary>
            /// <returns>The URI of the committed blob.</returns>
            public Uri Commit()
            {
                StagePendingData();

                _blockBlob.CommitBlockList(_blockIds);
                return _blockBlob.Uri;
            }

            public override void Flush()
            {
                // Data is uploaded when a block is full or on commit, not on flush.
            }

            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _pending.Dispose();
                }

                base.Dispose(disposing);
            }

            private void StagePendingData()
            {
                if (_pending.Length == 0)
                {
                    return;
                }

                // Block IDs must all have the same length within a blob. A GUID gives a unique, fixed-length ID.
                string blockId = Convert.ToBase64String(Guid.NewGuid().ToByteArray());

                _pending.Position = 0;
                try
                {
                    _blockBlob.StageBlock(blockId, _pending);
                }
                finally
                {
                    // If staging failed, keep appending after the pending data so a later attempt uploads all of it.
                    _pending.Position = _pending.Length;
                }

                _blockIds.Add(blockId);
                _pending.SetLength(0);
            }
        }
    }
}
