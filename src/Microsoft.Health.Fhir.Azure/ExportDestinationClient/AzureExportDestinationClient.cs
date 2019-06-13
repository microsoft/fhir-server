// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;

namespace Microsoft.Health.Fhir.Azure.ExportDestinationClient
{
    public class AzureExportDestinationClient : IExportDestinationClient
    {
        private CloudBlobClient _blobClient = null;
        private CloudBlobContainer _blobContainer = null;

        private Dictionary<Uri, CloudBlockBlobWrapper> _uriToBlobMapping = new Dictionary<Uri, CloudBlockBlobWrapper>();
        private Dictionary<(Uri FileUri, uint PartId), Stream> _streamMappings = new Dictionary<(Uri FileUri, uint PartId), Stream>();

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
            blockBlob.Properties.ContentType = "application/fhir+ndjson";
            _uriToBlobMapping.Add(blockBlob.Uri, new CloudBlockBlobWrapper(blockBlob));

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

            var uploadAndCommitTasks = new List<Task>();
            foreach (KeyValuePair<(Uri, uint), Stream> mapping in _streamMappings)
            {
                Stream stream = mapping.Value;

                // Reset the position.
                stream.Position = 0;

                CloudBlockBlobWrapper blobWrapper = _uriToBlobMapping[mapping.Key.Item1];

                var blockId = Convert.ToBase64String(Encoding.ASCII.GetBytes(mapping.Key.Item2.ToString("d6")));
                uploadAndCommitTasks.Add(Task.Run(async () =>
                {
                    await blobWrapper.UploadBlockAsync(blockId, stream, md5Hash: null, cancellationToken);
                    await blobWrapper.CommitBlockListAsync(cancellationToken);

                    stream.Dispose();
                }));
            }

            await Task.WhenAll(uploadAndCommitTasks);

            // We can clear the stream mappings once we commit everything in memory.
            _streamMappings.Clear();
        }

        private void CheckIfClientIsConnected()
        {
            if (_blobClient == null)
            {
                throw new DestinationConnectionException(Resources.DestinationClientNotConnected);
            }
        }
    }
}
