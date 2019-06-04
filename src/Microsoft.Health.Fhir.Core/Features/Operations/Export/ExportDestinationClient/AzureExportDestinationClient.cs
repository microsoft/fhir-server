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

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient
{
    public class AzureExportDestinationClient : IExportDestinationClient
    {
        private CloudBlobClient _blobClient = null;
        private CloudBlobContainer _blobContainer = null;

        private Dictionary<Uri, CloudBlockBlobWrapper> _uriToBlobMapping = new Dictionary<Uri, CloudBlockBlobWrapper>();
        private Dictionary<(Uri FileUri, uint PartId), Stream> _streamMappings = new Dictionary<(Uri FileUri, uint PartId), Stream>();

        public string DestinationType => "azure-block-blob";

        public async Task ConnectAsync(string connectionSettings, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrWhiteSpace(connectionSettings, nameof(connectionSettings));

            CloudStorageAccount cloudAccount = null;
            if (!CloudStorageAccount.TryParse(connectionSettings, out cloudAccount))
            {
                throw new CantConnectToDestinationException();
            }

            _blobClient = cloudAccount.CreateCloudBlobClient();

            // We will need to accept a container name/reference instead of using root container.
            _blobContainer = _blobClient.GetRootContainerReference();
            await _blobContainer.CreateIfNotExistsAsync();
        }

        public Task<Uri> CreateFileAsync(string fileName, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrWhiteSpace(fileName, nameof(fileName));
            CheckIfClientIsConnected();

            CloudBlockBlob blockBlob = _blobContainer.GetBlockBlobReference(fileName);
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

            foreach (KeyValuePair<(Uri, uint), Stream> mapping in _streamMappings)
            {
                Stream stream = mapping.Value;

                // Reset the position.
                stream.Position = 0;

                CloudBlockBlobWrapper blobWrapper = _uriToBlobMapping[mapping.Key.Item1];

                var blockId = Convert.ToBase64String(Encoding.ASCII.GetBytes(mapping.Key.Item2.ToString("d6")));
                await blobWrapper.UploadBlockAsync(blockId, mapping.Value, md5Hash: null, cancellationToken);

                await blobWrapper.CommitBlockListAsync(cancellationToken);
            }

            // We can clear the stream mappings once we commit everything in memory.
            _streamMappings.Clear();
        }

        private void CheckIfClientIsConnected()
        {
            if (_blobClient == null)
            {
                throw new DestinationClientNotConnectedException();
            }
        }
    }
}
