// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Operations;

namespace Microsoft.Health.Fhir.Azure.IntegrationDataStore
{
    public class AzureBlobIntegrationDataStoreClient : IIntegrationDataStoreClient
    {
        private IIntegrationDataStoreClientInitilizer<CloudBlobClient> _integrationDataStoreClientInitializer;
        private ILogger<AzureBlobIntegrationDataStoreClient> _logger;

        public AzureBlobIntegrationDataStoreClient(
            IIntegrationDataStoreClientInitilizer<CloudBlobClient> integrationDataStoreClientInitializer,
            ILogger<AzureBlobIntegrationDataStoreClient> logger)
        {
            _integrationDataStoreClientInitializer = integrationDataStoreClientInitializer;
            _logger = logger;
        }

        public Stream DownloadResource(Uri blobUri, long startOffset, CancellationToken cancellationToken)
        {
            return new AzureBlobSourceStream(async () => await GetCloudBlobClientFromServerAsync(blobUri, cancellationToken), startOffset, _logger);
        }

        public async Task PrepareResourceAsync(Uri resourceUri, CancellationToken cancellationToken)
        {
            try
            {
                ICloudBlob blob = await GetCloudBlobClientAsync(resourceUri, cancellationToken);
                await blob.Container.CreateIfNotExistsAsync(cancellationToken);
            }
            catch (StorageException storageEx)
            {
                _logger.LogError(storageEx, $"Failed to create container for {resourceUri}");

                throw;
            }
        }

        public async Task UploadPartDataAsync(Uri resourceUri, Stream stream, long partId, CancellationToken cancellationToken)
        {
            try
            {
                var blockId = Convert.ToBase64String(BitConverter.GetBytes(partId));
                CloudBlockBlob blob = await GetCloudBlobClientAsync(resourceUri, cancellationToken);

                await blob.PutBlockAsync(blockId, stream);
            }
            catch (StorageException storageEx)
            {
                _logger.LogError(storageEx, $"Failed to update part data for {resourceUri} of {partId}");

                throw;
            }
        }

        public async Task CommitDataAsync(Uri resourceUri, long[] partIds, CancellationToken cancellationToken)
        {
            try
            {
                var blockIds = partIds.Select(id => Convert.ToBase64String(BitConverter.GetBytes(id)));
                CloudBlockBlob blob = await GetCloudBlobClientAsync(resourceUri, cancellationToken);

                await blob.PutBlockListAsync(blockIds, cancellationToken);
            }
            catch (StorageException storageEx)
            {
                _logger.LogError(storageEx, $"Failed to update part data for {resourceUri}");

                throw;
            }
        }

        private async Task<CloudBlockBlob> GetCloudBlobClientAsync(Uri blobUri, CancellationToken cancellationToken)
        {
            CloudBlobClient cloudBlobClient = await _integrationDataStoreClientInitializer.GetAuthorizedClientAsync(cancellationToken);
            return new CloudBlockBlob(blobUri, cloudBlobClient);
        }

        private async Task<ICloudBlob> GetCloudBlobClientFromServerAsync(Uri blobUri, CancellationToken cancellationToken)
        {
            CloudBlobClient cloudBlobClient = await _integrationDataStoreClientInitializer.GetAuthorizedClientAsync(cancellationToken);
            return await cloudBlobClient.GetBlobReferenceFromServerAsync(blobUri);
        }
    }
}
