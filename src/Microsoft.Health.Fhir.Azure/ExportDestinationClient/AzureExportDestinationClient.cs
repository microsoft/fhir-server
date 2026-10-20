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

            if (!_blobWriters.TryGetValue(fileName, out BlockBlobWriter blobWriter))
            {
                blobWriter = new BlockBlobWriter(_blobContainer.GetBlockBlobClient(fileName));
                _blobWriters.Add(fileName, blobWriter);
            }

            blobWriter.WriteLine(data);
        }

        public IDictionary<string, Uri> Commit()
        {
            Dictionary<string, Uri> blobUris = new Dictionary<string, Uri>();

            foreach (KeyValuePair<string, BlockBlobWriter> entry in _blobWriters)
            {
                blobUris.Add(entry.Key, CommitWriter(entry.Key, entry.Value));
            }

            // Release the writers only after every file is committed. If one file fails, the caller doesn't mark any file
            // as committed and the export job retry keeps writing to the same files, so already committed writers must
            // keep their block lists. Otherwise a new writer would replace the committed blob with only the later data.
            foreach (BlockBlobWriter blobWriter in _blobWriters.Values)
            {
                blobWriter.Dispose();
            }

            _blobWriters.Clear();
            return blobUris;
        }

        public Uri CommitFile(string fileName)
        {
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
                    // The writer is kept so that a later commit, for example from the export job retry, can still succeed.
                    _logger.LogError(ex2, "Failed to write export file {FileName} on retry", fileName);
                    throw new DestinationConnectionException(ex2.Message, (HttpStatusCode)ex2.Status);
                }
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
        /// Buffers the lines of one export file and uploads them as the blocks of a block blob.
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
            // Matches the default buffer size of OpenWrite, so memory use per open file is unchanged.
            private const int BlockSizeInBytes = 4 * 1024 * 1024;

            private readonly BlockBlobClient _blockBlob;
            private readonly MemoryStream _buffer = new MemoryStream();
            private readonly StreamWriter _streamWriter;
            private readonly List<string> _blockIds = new List<string>();

            public BlockBlobWriter(BlockBlobClient blockBlob)
            {
                _blockBlob = blockBlob;
                _streamWriter = new StreamWriter(_buffer, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 1024, leaveOpen: true);
            }

            /// <summary>
            /// Writes a line to the file and uploads a block when the buffer is full.
            /// </summary>
            /// <param name="data">The line to write.</param>
            public void WriteLine(string data)
            {
                _streamWriter.WriteLine(data);
                _streamWriter.Flush();

                if (_buffer.Length >= BlockSizeInBytes)
                {
                    StageBufferedData();
                }
            }

            /// <summary>
            /// Uploads any buffered data and commits all blocks, without an If-Match condition.
            /// </summary>
            /// <returns>The URI of the committed blob.</returns>
            public Uri Commit()
            {
                _streamWriter.Flush();
                StageBufferedData();

                _blockBlob.CommitBlockList(_blockIds);
                return _blockBlob.Uri;
            }

            public void Dispose()
            {
                _streamWriter.Dispose();
                _buffer.Dispose();
            }

            private void StageBufferedData()
            {
                if (_buffer.Length == 0)
                {
                    return;
                }

                // Block IDs must all have the same length within a blob. A GUID gives a unique, fixed-length ID.
                string blockId = Convert.ToBase64String(Guid.NewGuid().ToByteArray());

                _buffer.Position = 0;
                try
                {
                    _blockBlob.StageBlock(blockId, _buffer);
                }
                finally
                {
                    // If staging failed, keep appending after the buffered data so a later attempt uploads all of it.
                    _buffer.Position = _buffer.Length;
                }

                _blockIds.Add(blockId);
                _buffer.SetLength(0);
            }
        }
    }
}
