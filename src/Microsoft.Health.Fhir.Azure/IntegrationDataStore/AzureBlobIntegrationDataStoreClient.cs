// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Azure.ExportDestinationClient;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations;

namespace Microsoft.Health.Fhir.Azure.IntegrationDataStore
{
    public class AzureBlobIntegrationDataStoreClient : IIntegrationDataStoreClient
    {
        private IIntegrationDataStoreClientInitilizer<CloudBlobClient> _integrationDataStoreClientInitializer;
        private IntegrationStoreRetryExceptionPolicyFactory _integrationStoreRetryExceptionPolicyFactory;
        private ILogger<AzureBlobIntegrationDataStoreClient> _logger;

        public AzureBlobIntegrationDataStoreClient(
            IIntegrationDataStoreClientInitilizer<CloudBlobClient> integrationDataStoreClientInitializer,
            IOptions<IntegrationDataStoreConfiguration> integrationDataStoreConfiguration,
            ILogger<AzureBlobIntegrationDataStoreClient> logger)
        {
            EnsureArg.IsNotNull(integrationDataStoreClientInitializer, nameof(integrationDataStoreClientInitializer));
            EnsureArg.IsNotNull(integrationDataStoreConfiguration?.Value, nameof(integrationDataStoreConfiguration));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _integrationDataStoreClientInitializer = integrationDataStoreClientInitializer;
            _integrationStoreRetryExceptionPolicyFactory = new IntegrationStoreRetryExceptionPolicyFactory(integrationDataStoreConfiguration);
            _logger = logger;
        }

        public Stream DownloadResource(Uri resourceUri, long startOffset, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(resourceUri, nameof(resourceUri));

            return new AzureBlobSourceStream(async () => await GetCloudBlobClientFromServerAsync(resourceUri, cancellationToken), startOffset, _logger);
        }

        public async Task<Uri> PrepareResourceAsync(string containerId, string fileName, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrEmpty(containerId, nameof(containerId));
            EnsureArg.IsNotNullOrEmpty(fileName, nameof(fileName));

            try
            {
                return await _integrationStoreRetryExceptionPolicyFactory
                            .RetryPolicy
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
                _logger.LogInformation(storageEx, "Failed to create container for {Container}:{File}", containerId, fileName);

                HttpStatusCode statusCode = StorageExceptionParser.ParseStorageException(storageEx);
                throw new IntegrationDataStoreException(storageEx.Message, statusCode);
            }
        }

        public async Task UploadBlockAsync(Uri resourceUri, Stream stream, string blockId, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(resourceUri, nameof(resourceUri));
            EnsureArg.IsNotNull(stream, nameof(stream));
            EnsureArg.IsNotNullOrEmpty(blockId, nameof(blockId));

            try
            {
                await _integrationStoreRetryExceptionPolicyFactory
                            .RetryPolicy
                            .ExecuteAsync(async () =>
                            {
                                CloudBlockBlob blob = await GetCloudBlobClientAsync(resourceUri, cancellationToken);
                                await UploadBlockInternalAsync(blob, stream, blockId, cancellationToken);
                            });
            }
            catch (StorageException storageEx)
            {
                _logger.LogInformation(storageEx, "Failed to upload data for {Url}", resourceUri);

                HttpStatusCode statusCode = StorageExceptionParser.ParseStorageException(storageEx);
                throw new IntegrationDataStoreException(storageEx.Message, statusCode);
            }
        }

        public async Task CommitAsync(Uri resourceUri, string[] blockIds, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(resourceUri, nameof(resourceUri));
            EnsureArg.IsNotNull(blockIds, nameof(blockIds));

            try
            {
                await _integrationStoreRetryExceptionPolicyFactory
                            .RetryPolicy
                            .ExecuteAsync(async () =>
                            {
                                CloudBlockBlob blob = await GetCloudBlobClientAsync(resourceUri, cancellationToken);
                                await CommitInternalAsync(blob, blockIds, cancellationToken);
                            });
            }
            catch (StorageException storageEx)
            {
                _logger.LogInformation(storageEx, "Failed to commit for {Url}", resourceUri);

                HttpStatusCode statusCode = StorageExceptionParser.ParseStorageException(storageEx);
                throw new IntegrationDataStoreException(storageEx.Message, statusCode);
            }
        }

        public async Task AppendCommitAsync(Uri resourceUri, string[] blockIds, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(resourceUri, nameof(resourceUri));
            EnsureArg.IsNotNull(blockIds, nameof(blockIds));

            try
            {
                await _integrationStoreRetryExceptionPolicyFactory
                            .RetryPolicy
                            .ExecuteAsync(async () =>
                            {
                                CloudBlockBlob blob = await GetCloudBlobClientAsync(resourceUri, cancellationToken);
                                await AppendCommitInternalAsync(blob, blockIds, cancellationToken);
                            });
            }
            catch (StorageException storageEx)
            {
                _logger.LogInformation(storageEx, "Failed to append commit for {Url}", resourceUri);

                HttpStatusCode statusCode = StorageExceptionParser.ParseStorageException(storageEx);
                throw new IntegrationDataStoreException(storageEx.Message, statusCode);
            }
        }

        public async Task<Dictionary<string, object>> GetPropertiesAsync(Uri resourceUri, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(resourceUri, nameof(resourceUri));

            try
            {
                return await _integrationStoreRetryExceptionPolicyFactory
                            .RetryPolicy
                            .ExecuteAsync(async () =>
                            {
                                CloudBlobClient cloudBlobClient = await _integrationDataStoreClientInitializer.GetAuthorizedClientAsync(cancellationToken);
                                ICloudBlob blob = await cloudBlobClient.GetBlobReferenceFromServerAsync(resourceUri);

                                Dictionary<string, object> result = new Dictionary<string, object>();
                                result[IntegrationDataStoreClientConstants.BlobPropertyETag] = blob.Properties.ETag;
                                result[IntegrationDataStoreClientConstants.BlobPropertyLength] = blob.Properties.Length;

                                return result;
                            });
            }
            catch (StorageException storageEx)
            {
                _logger.LogInformation(storageEx, "Failed to get properties of blob {Url}", resourceUri);

                HttpStatusCode statusCode = StorageExceptionParser.ParseStorageException(storageEx);
                throw new IntegrationDataStoreException(storageEx.Message, statusCode);
            }
        }

        public async Task<string> TryAcquireLeaseAsync(Uri resourceUri, string proposedLeaseId, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(resourceUri, nameof(resourceUri));

            try
            {
                CloudBlockBlob blob = await GetCloudBlobClientAsync(resourceUri, cancellationToken);
                return await blob.AcquireLeaseAsync(null, proposedLeaseId, cancellationToken);
            }
            catch (StorageException storageEx)
            {
                _logger.LogInformation(storageEx, "Failed to acquire lease on the blob {Url}", resourceUri);
                return null;
            }
        }

        public async Task TryReleaseLeaseAsync(Uri resourceUri, string leaseId, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(resourceUri, nameof(resourceUri));

            try
            {
                CloudBlockBlob blob = await GetCloudBlobClientAsync(resourceUri, cancellationToken);
                await blob.ReleaseLeaseAsync(AccessCondition.GenerateLeaseCondition(leaseId), cancellationToken);
            }
            catch (Exception storageEx)
            {
                _logger.LogInformation(storageEx, "Failed to release lease on the blob {Url}", resourceUri);
            }
        }

        private static async Task AppendCommitInternalAsync(CloudBlockBlob blob, string[] blockIds, CancellationToken cancellationToken)
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
            return await cloudBlobClient.GetBlobReferenceFromServerAsync(blobUri, cancellationToken);
        }
    }
}
