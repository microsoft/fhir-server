// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Polly;

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

        public async Task<Uri> PrepareResourceAsync(string containerId, string fileName, CancellationToken cancellationToken)
        {
            try
            {
                return await Policy.Handle<StorageException>()
                    .WaitAndRetryAsync(
                        retryCount: 3,
                        sleepDurationProvider: (retryCount) => TimeSpan.FromSeconds(5 * (retryCount - 1)))
                    .ExecuteAsync(async () =>
                        {
                            CloudBlobClient cloudBlobClient = await _integrationDataStoreClientInitializer.GetAuthorizedClientAsync(cancellationToken);
                            CloudBlobContainer container = cloudBlobClient.GetContainerReference(containerId);

                            await container.CreateIfNotExistsAsync(cancellationToken);

                            CloudBlob blob = container.GetBlobReference(fileName);
                            return blob.Uri;
                        });
            }
            catch (StorageException storageEx)
            {
                _logger.LogError(storageEx, "Failed to create container for {0}:{1}", containerId, fileName);

                throw;
            }
        }

        public async Task UploadBlockAsync(Uri resourceUri, Stream stream, string blockId, CancellationToken cancellationToken)
        {
            try
            {
                await Policy.Handle<StorageException>()
                    .WaitAndRetryAsync(
                        retryCount: 3,
                        sleepDurationProvider: (retryCount) => TimeSpan.FromSeconds(5 * (retryCount - 1)))
                    .ExecuteAsync(async () =>
                    {
                        CloudBlockBlob blob = await GetCloudBlobClientAsync(resourceUri, cancellationToken);
                        await UploadBlockInternalAsync(blob, stream, blockId, cancellationToken);
                    });
            }
            catch (StorageException storageEx)
            {
                _logger.LogError(storageEx, "Failed to commit for {0}", resourceUri);

                throw;
            }
        }

        public async Task CommitAsync(Uri resourceUri, string[] blockIds, CancellationToken cancellationToken)
        {
            try
            {
                await Policy.Handle<StorageException>()
                    .WaitAndRetryAsync(
                        retryCount: 3,
                        sleepDurationProvider: (retryCount) => TimeSpan.FromSeconds(5 * (retryCount - 1)))
                    .ExecuteAsync(async () =>
                    {
                        CloudBlockBlob blob = await GetCloudBlobClientAsync(resourceUri, cancellationToken);
                        await CommitInternalAsync(blob, blockIds, cancellationToken);
                    });
            }
            catch (StorageException storageEx)
            {
                _logger.LogError(storageEx, "Failed to commit for {0}", resourceUri);

                throw;
            }
        }

        public async Task AppendCommitAsync(Uri resourceUri, string[] blockIds, CancellationToken cancellationToken)
        {
            try
            {
                await Policy.Handle<StorageException>()
                    .WaitAndRetryAsync(
                        retryCount: 2,
                        sleepDurationProvider: (retryCount) => TimeSpan.FromSeconds(5 * (retryCount - 1)))
                    .ExecuteAsync(async () =>
                    {
                        CloudBlockBlob blob = await GetCloudBlobClientAsync(resourceUri, cancellationToken);
                        await AppendCommitInternalAsync(blob, blockIds, cancellationToken);
                    });
            }
            catch (StorageException storageEx)
            {
                _logger.LogError(storageEx, "Failed to append commit for {0}", resourceUri);

                throw;
            }
        }

        public async Task<T> GetBlockPropertyAsync<T>(string blobUri, string propertyName, CancellationToken cancellationToken)
        {
            try
            {
                return await Policy.Handle<StorageException>()
                    .WaitAndRetryAsync(
                        retryCount: 3,
                        sleepDurationProvider: (retryCount) => TimeSpan.FromSeconds(5 * (retryCount - 1)))
                    .ExecuteAsync(async () =>
                    {
                        CloudBlobClient cloudBlobClient = await _integrationDataStoreClientInitializer.GetAuthorizedClientAsync(cancellationToken);
                        ICloudBlob blob = cloudBlobClient.GetBlobReferenceFromServer(new Uri(blobUri));
                        var value = blob.Properties.GetType().GetProperty(propertyName).GetValue(blob.Properties);
                        return value == null ? default(T) : (T)value;
                    });
            }
            catch (StorageException storageEx)
            {
                _logger.LogError(storageEx, "Failed to get property {0} of blob {1}", propertyName, blobUri);

                throw;
            }
        }

        private async Task AppendCommitInternalAsync(CloudBlockBlob blob, string[] blockIds, CancellationToken cancellationToken)
        {
            IEnumerable<ListBlockItem> blockList = await blob.DownloadBlockListAsync(
                                                            BlockListingFilter.Committed,
                                                            accessCondition: null,
                                                            options: null,
                                                            operationContext: null,
                                                            cancellationToken);

            List<string> newBlockLists = blockList.Select(b => b.Name).ToList();
            newBlockLists.AddRange(blockIds);

            await CommitInternalAsync(blob, newBlockLists.ToArray(), cancellationToken);
        }

        private static async Task UploadBlockInternalAsync(CloudBlockBlob blob, Stream stream, string blockId, CancellationToken cancellationToken)
        {
            await blob.PutBlockAsync(blockId, stream, contentMD5: null, cancellationToken);
        }

        private static async Task CommitInternalAsync(CloudBlockBlob blob, string[] blockIds, CancellationToken cancellationToken)
        {
            await blob.PutBlockListAsync(blockIds, cancellationToken);
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
