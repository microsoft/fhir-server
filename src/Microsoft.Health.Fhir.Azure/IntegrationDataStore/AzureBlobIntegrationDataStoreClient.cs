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
using Azure;
using Azure.Identity;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations;

namespace Microsoft.Health.Fhir.Azure.IntegrationDataStore
{
    public class AzureBlobIntegrationDataStoreClient : IIntegrationDataStoreClient
    {
        // Well-known storage emulator account info, not to be used in production (see https://learn.microsoft.com/en-us/azure/storage/common/storage-configure-connection-string?toc=%2Fazure%2Fstorage%2Fblobs%2Ftoc.json&bc=%2Fazure%2Fstorage%2Fblobs%2Fbreadcrumb%2Ftoc.json#configure-a-connection-string-for-azurite)
        private const string StorageEmulatorConnectionStringPrefix = "UseDevelopmentStorage";
        private const string StorageEmulatorAccountName = "devstoreaccount1";
        private const string StorageEmulatorAccountKey = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

        private IIntegrationDataStoreClientInitilizer<BlobServiceClient> _integrationDataStoreClientInitializer;
        private IntegrationDataStoreConfiguration _integrationDataStoreConfiguration;
        private IntegrationStoreRetryExceptionPolicyFactory _integrationStoreRetryExceptionPolicyFactory;
        private bool _isAccessTokenClientInitializer;
        private ILogger<AzureBlobIntegrationDataStoreClient> _logger;

        public AzureBlobIntegrationDataStoreClient(
            IIntegrationDataStoreClientInitilizer<BlobServiceClient> integrationDataStoreClientInitializer,
            IOptions<IntegrationDataStoreConfiguration> integrationDataStoreConfiguration,
            ILogger<AzureBlobIntegrationDataStoreClient> logger)
        {
            EnsureArg.IsNotNull(integrationDataStoreClientInitializer, nameof(integrationDataStoreClientInitializer));
            EnsureArg.IsNotNull(integrationDataStoreConfiguration?.Value, nameof(integrationDataStoreConfiguration));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _integrationDataStoreClientInitializer = integrationDataStoreClientInitializer;
            _integrationDataStoreConfiguration = integrationDataStoreConfiguration.Value;
            _integrationStoreRetryExceptionPolicyFactory = new IntegrationStoreRetryExceptionPolicyFactory(integrationDataStoreConfiguration);
            _isAccessTokenClientInitializer = _integrationDataStoreClientInitializer is AzureAccessTokenClientInitializerV2;
            _logger = logger;
        }

        public Stream DownloadResource(Uri resourceUri, long startOffset, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(resourceUri, nameof(resourceUri));

            return new AzureBlobSourceStream(() => GetBlobClient(resourceUri), startOffset, _logger);
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
                                    BlobServiceClient blobClient = await _integrationDataStoreClientInitializer.GetAuthorizedClientAsync();

                                    try
                                    {
                                        BlobContainerClient blobContainer = blobClient.GetBlobContainerClient(containerId);
                                        await blobContainer.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

                                        BlobClient blob = blobContainer.GetBlobClient(fileName);
                                        return blob.Uri;
                                    }
                                    catch (RequestFailedException se)
                                    {
                                        _logger.LogWarning(se, "{Error}", se.Message);

                                        throw;
                                    }
                                });
            }
            catch (RequestFailedException se)
            {
                _logger.LogInformation(se, "Failed to create container for {Container}:{File}", containerId, fileName);

                throw new IntegrationDataStoreException(se.Message, (HttpStatusCode)se.Status);
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
                                BlockBlobClient blob = GetBlockBlobClient(resourceUri);
                                await UploadBlockInternalAsync(blob, stream, blockId, cancellationToken);
                            });
            }
            catch (RequestFailedException se)
            {
                _logger.LogInformation(se, "Failed to upload data for {Url}", resourceUri);

                throw new IntegrationDataStoreException(se.Message, (HttpStatusCode)se.Status);
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
                                BlockBlobClient blob = GetBlockBlobClient(resourceUri);
                                await CommitInternalAsync(blob, blockIds, cancellationToken);
                            });
            }
            catch (RequestFailedException se)
            {
                _logger.LogInformation(se, "Failed to commit for {Url}", resourceUri);

                throw new IntegrationDataStoreException(se.Message, (HttpStatusCode)se.Status);
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
                                BlockBlobClient blob = GetBlockBlobClient(resourceUri);
                                await AppendCommitInternalAsync(blob, blockIds, cancellationToken);
                            });
            }
            catch (RequestFailedException se)
            {
                _logger.LogInformation(se, "Failed to append commit for {Url}", resourceUri);

                throw new IntegrationDataStoreException(se.Message, (HttpStatusCode)se.Status);
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
                                BlobClient blobClient = GetBlobClient(resourceUri);
                                Response<BlobProperties> response = await blobClient.GetPropertiesAsync(null, cancellationToken);
                                Dictionary<string, object> result = new Dictionary<string, object>();
                                result[IntegrationDataStoreClientConstants.BlobPropertyETag] = response.Value.ETag.ToString();
                                result[IntegrationDataStoreClientConstants.BlobPropertyLength] = response.Value.ContentLength;

                                return result;
                            });
            }
            catch (RequestFailedException se)
            {
                _logger.LogInformation(se, "Failed to get properties of blob {Url}", resourceUri);

                throw new IntegrationDataStoreException(se.Message, (HttpStatusCode)se.Status);
            }
        }

        public async Task<string> TryAcquireLeaseAsync(Uri resourceUri, string proposedLeaseId, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(resourceUri, nameof(resourceUri));

            try
            {
                BlockBlobClient blob = GetBlockBlobClient(resourceUri);
                BlobLeaseClient lease = blob.GetBlobLeaseClient(proposedLeaseId);
                Response<BlobLease> response = await lease.AcquireAsync(BlobLeaseClient.InfiniteLeaseDuration, null, cancellationToken);
                return response?.Value?.LeaseId;
            }
            catch (RequestFailedException se)
            {
                _logger.LogInformation(se, "Failed to acquire lease on the blob {Url}", resourceUri);
                return null;
            }
        }

        public async Task TryReleaseLeaseAsync(Uri resourceUri, string leaseId, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(resourceUri, nameof(resourceUri));

            try
            {
                BlockBlobClient blob = GetBlockBlobClient(resourceUri);
                BlobLeaseClient lease = blob.GetBlobLeaseClient(leaseId);
                await lease.ReleaseAsync(null, cancellationToken);
            }
            catch (RequestFailedException se)
            {
                _logger.LogInformation(se, "Failed to release lease on the blob {Url}", resourceUri);
            }
        }

        private static async Task AppendCommitInternalAsync(BlockBlobClient blob, string[] blockIds, CancellationToken cancellationToken)
        {
            Response<BlockList> blockList = await blob.GetBlockListAsync(
                                                            BlockListTypes.Committed,
                                                            snapshot: null,
                                                            conditions: null,
                                                            cancellationToken);

            List<string> newBlockLists = blockList.Value.CommittedBlocks.Select(b => b.Name).ToList();
            newBlockLists.AddRange(blockIds);

            await CommitInternalAsync(blob, newBlockLists.ToArray(), cancellationToken);
        }

        private static async Task UploadBlockInternalAsync(BlockBlobClient blob, Stream stream, string blockId, CancellationToken cancellationToken)
        {
            await blob.StageBlockAsync(blockId, stream, null, cancellationToken);
        }

        private static async Task CommitInternalAsync(BlockBlobClient blob, string[] blockIds, CancellationToken cancellationToken)
        {
            await blob.CommitBlockListAsync(blockIds, null, cancellationToken);
        }

        private BlockBlobClient GetBlockBlobClient(Uri blobUri)
        {
            if (_isAccessTokenClientInitializer)
            {
                return new BlockBlobClient(blobUri, new DefaultAzureCredential());
            }

            return new BlockBlobClient(blobUri, GetSharedKeyCredential(_integrationDataStoreConfiguration.StorageAccountConnection));
        }

        private BlobClient GetBlobClient(Uri blobUri)
        {
            if (_isAccessTokenClientInitializer)
            {
                return new BlobClient(blobUri, new DefaultAzureCredential());
            }

            return new BlobClient(blobUri, GetSharedKeyCredential(_integrationDataStoreConfiguration.StorageAccountConnection));
        }

        private static StorageSharedKeyCredential GetSharedKeyCredential(string connectionString)
        {
            EnsureArg.IsNotEmptyOrWhiteSpace(connectionString, nameof(connectionString));
            if (connectionString.StartsWith(StorageEmulatorConnectionStringPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return new StorageSharedKeyCredential(StorageEmulatorAccountName, StorageEmulatorAccountKey);
            }

            string[] segments = connectionString.Split(";");
            string accountName = null;
            string accountKey = null;
            foreach (var segment in segments)
            {
                int index = segment.IndexOf('=', StringComparison.Ordinal);
                if (index >= 0)
                {
                    string key = segment.Substring(0, index);
                    if (key.Equals("AccountName", StringComparison.OrdinalIgnoreCase))
                    {
                        accountName = segment.Substring(index + 1);
                    }
                    else if (key.Equals("AccountKey", StringComparison.OrdinalIgnoreCase))
                    {
                        accountKey = segment.Substring(index + 1);
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(accountName) || string.IsNullOrWhiteSpace(accountKey))
            {
                throw new ArgumentException("Invalid connection string.", nameof(connectionString));
            }

            return new StorageSharedKeyCredential(accountName, accountKey);
        }
    }
}
