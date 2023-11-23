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
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Operations;

namespace Microsoft.Health.Fhir.Azure.IntegrationDataStore
{
    public class AzureBlobIntegrationDataStoreClient : IIntegrationDataStoreClient
    {
        private IIntegrationDataStoreClientInitializer _integrationDataStoreClientInitializer;
        private IntegrationDataStoreConfiguration _integrationDataStoreConfiguration;
        private IntegrationStoreRetryExceptionPolicyFactory _integrationStoreRetryExceptionPolicyFactory;
        private ILogger<AzureBlobIntegrationDataStoreClient> _logger;

        public AzureBlobIntegrationDataStoreClient(
            IIntegrationDataStoreClientInitializer integrationDataStoreClientInitializer,
            IOptions<IntegrationDataStoreConfiguration> integrationDataStoreConfiguration,
            ILogger<AzureBlobIntegrationDataStoreClient> logger)
        {
            EnsureArg.IsNotNull(integrationDataStoreClientInitializer, nameof(integrationDataStoreClientInitializer));
            EnsureArg.IsNotNull(integrationDataStoreConfiguration?.Value, nameof(integrationDataStoreConfiguration));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _integrationDataStoreClientInitializer = integrationDataStoreClientInitializer;
            _integrationDataStoreConfiguration = integrationDataStoreConfiguration.Value;
            _integrationStoreRetryExceptionPolicyFactory = new IntegrationStoreRetryExceptionPolicyFactory(integrationDataStoreConfiguration);
            _logger = logger;
        }

        public Stream DownloadResource(Uri resourceUri, long startOffset, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(resourceUri, nameof(resourceUri));

            return new AzureBlobSourceStream(async () => await _integrationDataStoreClientInitializer.GetAuthorizedBlobClientAsync(resourceUri), startOffset, _logger);
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
                Exception exception = HandleRequestFailedException(se, "Failed to create container for {Container}:{File}", containerId, fileName);

                throw new IntegrationDataStoreException(exception, (HttpStatusCode)se.Status);
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
                                BlockBlobClient blob = await _integrationDataStoreClientInitializer.GetAuthorizedBlockBlobClientAsync(resourceUri);
                                await UploadBlockInternalAsync(blob, stream, blockId, cancellationToken);
                            });
            }
            catch (RequestFailedException se)
            {
                Exception exception = HandleRequestFailedException(se, "Failed to upload data for {Url}", resourceUri);

                throw new IntegrationDataStoreException(exception, (HttpStatusCode)se.Status);
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
                                BlockBlobClient blob = await _integrationDataStoreClientInitializer.GetAuthorizedBlockBlobClientAsync(resourceUri);
                                await CommitInternalAsync(blob, blockIds, cancellationToken);
                            });
            }
            catch (RequestFailedException se)
            {
                Exception exception = HandleRequestFailedException(se, "Failed to commit for {Url}", resourceUri);

                throw new IntegrationDataStoreException(exception, (HttpStatusCode)se.Status);
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
                                BlockBlobClient blob = await _integrationDataStoreClientInitializer.GetAuthorizedBlockBlobClientAsync(resourceUri);
                                await AppendCommitInternalAsync(blob, blockIds, cancellationToken);
                            });
            }
            catch (RequestFailedException se)
            {
                Exception exception = HandleRequestFailedException(se, "Failed to append commit for {Url}", resourceUri);

                throw new IntegrationDataStoreException(exception, (HttpStatusCode)se.Status);
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
                                BlobClient blobClient = await _integrationDataStoreClientInitializer.GetAuthorizedBlobClientAsync(resourceUri);
                                Response<BlobProperties> response = await blobClient.GetPropertiesAsync(null, cancellationToken);
                                Dictionary<string, object> result = new Dictionary<string, object>();
                                result[IntegrationDataStoreClientConstants.BlobPropertyETag] = response.Value.ETag.ToString();
                                result[IntegrationDataStoreClientConstants.BlobPropertyLength] = response.Value.ContentLength;

                                return result;
                            });
            }
            catch (RequestFailedException se)
            {
                Exception exception = HandleRequestFailedException(se, "Failed to get properties of blob {Url}", resourceUri);

                throw new IntegrationDataStoreException(exception, (HttpStatusCode)se.Status);
            }
        }

        public async Task<string> TryAcquireLeaseAsync(Uri resourceUri, string proposedLeaseId, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(resourceUri, nameof(resourceUri));

            try
            {
                BlockBlobClient blob = await _integrationDataStoreClientInitializer.GetAuthorizedBlockBlobClientAsync(resourceUri);
                BlobLeaseClient lease = blob.GetBlobLeaseClient(proposedLeaseId);
                Response<BlobLease> response = await lease.AcquireAsync(BlobLeaseClient.InfiniteLeaseDuration, null, cancellationToken);
                return response?.Value?.LeaseId;
            }
            catch (RequestFailedException se)
            {
                HandleRequestFailedException(se, "Failed to acquire lease on the blob {Url}", resourceUri);

                return null;
            }
        }

        public async Task TryReleaseLeaseAsync(Uri resourceUri, string leaseId, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(resourceUri, nameof(resourceUri));

            try
            {
                BlockBlobClient blob = await _integrationDataStoreClientInitializer.GetAuthorizedBlockBlobClientAsync(resourceUri);
                BlobLeaseClient lease = blob.GetBlobLeaseClient(leaseId);
                await lease.ReleaseAsync(null, cancellationToken);
            }
            catch (RequestFailedException se)
            {
                HandleRequestFailedException(se, "Failed to release lease on the blob {Url}", resourceUri);
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

        private Exception HandleRequestFailedException(RequestFailedException requestFailedException, string message, params object[] args)
        {
            Exception finalException = requestFailedException;

            // 'AuthorizationPermissionMismatch' is raised when a request is not authorized to perform an operation.
            // As 'RequestFailedException' is too generic, a more specific type of exception needs to be used to identify non-actionable scenarios.
            if (string.Equals(requestFailedException.ErrorCode, "AuthorizationPermissionMismatch", StringComparison.OrdinalIgnoreCase))
            {
                finalException = new InsufficientAccessException(string.Format(message, args), requestFailedException);
            }

            _logger.LogInformation(finalException, message, args);

            return finalException;
        }
    }
}
