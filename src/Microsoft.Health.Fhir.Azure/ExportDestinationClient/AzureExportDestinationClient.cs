// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;

namespace Microsoft.Health.Fhir.Azure.ExportDestinationClient
{
    public class AzureExportDestinationClient : IExportDestinationClient
    {
        private CloudBlobClient _blobClient = null;
        private CloudBlobContainer _blobContainer = null;

        private Dictionary<Uri, CloudBlockBlobWrapper> _uriToBlobMapping = new Dictionary<Uri, CloudBlockBlobWrapper>();
        private Dictionary<(Uri FileUri, uint PartId), Stream> _streamMappings = new Dictionary<(Uri FileUri, uint PartId), Stream>();

        private readonly ILogger _logger;

        public AzureExportDestinationClient(ILogger<AzureExportDestinationClient> logger)
        {
            EnsureArg.IsNotNull(logger, nameof(logger));

            _logger = logger;
        }

        public string DestinationType => "azure-block-blob";

        public async Task ConnectAsync(string connectionSettings, CancellationToken cancellationToken, string containerId = null)
        {
            EnsureArg.IsNotNullOrWhiteSpace(connectionSettings, nameof(connectionSettings));

            string decodedConnectionString;
            try
            {
                decodedConnectionString = Encoding.UTF8.GetString(Convert.FromBase64String(connectionSettings));
            }
            catch (Exception)
            {
                throw new DestinationConnectionException(Resources.InvalidConnectionSettings);
            }

            if (!CloudStorageAccount.TryParse(decodedConnectionString, out CloudStorageAccount cloudAccount))
            {
                throw new DestinationConnectionException(Resources.CantConnectToDestination);
            }

            _blobClient = cloudAccount.CreateCloudBlobClient();

            // Use root container if no container id has been provided.
            if (string.IsNullOrWhiteSpace(containerId))
            {
                _blobContainer = _blobClient.GetRootContainerReference();
            }
            else
            {
                _blobContainer = _blobClient.GetContainerReference(containerId);
            }

            await _blobContainer.CreateIfNotExistsAsync();
        }

        public Task<Uri> CreateFileAsync(string fileName, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrWhiteSpace(fileName, nameof(fileName));
            CheckIfClientIsConnected();

            CloudBlockBlob blockBlob = _blobContainer.GetBlockBlobReference(fileName);

            if (!_uriToBlobMapping.ContainsKey(blockBlob.Uri))
            {
                blockBlob.Properties.ContentType = "application/fhir+ndjson";
                _uriToBlobMapping.Add(blockBlob.Uri, new CloudBlockBlobWrapper(blockBlob));
            }

            return Task.FromResult(blockBlob.Uri);
        }

        public async Task WriteFilePartAsync(Uri fileUri, uint partId, byte[] bytes, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(fileUri, nameof(fileUri));
            EnsureArg.IsNotNull(bytes, nameof(bytes));
            CheckIfClientIsConnected();

            var key = (fileUri, partId);

            if (!_streamMappings.TryGetValue(key, out Stream stream))
            {
                stream = new MemoryStream();
                _streamMappings.Add(key, stream);
            }

            await stream.WriteAsync(bytes, cancellationToken);
        }

        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            CheckIfClientIsConnected();

            // Upload all blocks for each blob that was modified.
            Task[] uploadTasks = new Task[_streamMappings.Count];
            CloudBlockBlobWrapper[] wrappersToCommit = new CloudBlockBlobWrapper[_streamMappings.Count];

            int index = 0;
            foreach (KeyValuePair<(Uri, uint), Stream> mapping in _streamMappings)
            {
                // Reset stream position.
                Stream stream = mapping.Value;
                stream.Position = 0;

                CloudBlockBlobWrapper blobWrapper = _uriToBlobMapping[mapping.Key.Item1];
                var blockId = Convert.ToBase64String(Encoding.ASCII.GetBytes(mapping.Key.Item2.ToString("d6")));

                uploadTasks[index] = blobWrapper.UploadBlockAsync(blockId, stream, md5Hash: null, cancellationToken);
                wrappersToCommit[index] = blobWrapper;
                index++;
            }

            await Task.WhenAll(uploadTasks);

            // Commit all the blobs that were uploaded.
            Task[] commitTasks = wrappersToCommit.Select(wrapper => wrapper.CommitBlockListAsync(cancellationToken)).ToArray();
            await Task.WhenAll(commitTasks);

            // We can clear the stream mappings once we commit everything.
            foreach (Stream stream in _streamMappings.Values)
            {
                stream.Dispose();
            }

            _streamMappings.Clear();
        }

        public async Task OpenFileAsync(Uri fileUri, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(fileUri, nameof(fileUri));
            CheckIfClientIsConnected();

            if (_uriToBlobMapping.ContainsKey(fileUri))
            {
                _logger.LogInformation("Trying to open a file that the client already knows about.");
                return;
            }

            var blob = new CloudBlockBlob(fileUri, _blobClient.Credentials);

            // We are going to consider only committed blocks.
            IEnumerable<ListBlockItem> result = await blob.DownloadBlockListAsync(
                BlockListingFilter.Committed,
                accessCondition: null,
                options: null,
                operationContext: null,
                cancellationToken);

            // Update the internal mapping with the block lists of the blob.
            var wrapper = new CloudBlockBlobWrapper(blob, result.Select(x => x.Name).ToList());
            _uriToBlobMapping.Add(fileUri, wrapper);
        }

        private void CheckIfClientIsConnected()
        {
            if (_blobClient == null || _blobContainer == null)
            {
                throw new DestinationConnectionException(Resources.DestinationClientNotConnected);
            }
        }
    }
}
