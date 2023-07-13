// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Threading.Tasks;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations;

namespace Microsoft.Health.Fhir.Azure.IntegrationDataStore
{
    public class AzureConnectionStringClientInitializerV2 : IIntegrationDataStoreClientInitializer
    {
        // Well-known storage emulator account info, not to be used in production (see https://learn.microsoft.com/en-us/azure/storage/common/storage-configure-connection-string?toc=%2Fazure%2Fstorage%2Fblobs%2Ftoc.json&bc=%2Fazure%2Fstorage%2Fblobs%2Fbreadcrumb%2Ftoc.json#configure-a-connection-string-for-azurite)
        private const string StorageEmulatorConnectionStringPrefix = "UseDevelopmentStorage";
        private const string StorageEmulatorAccountName = "devstoreaccount1";
        private const string StorageEmulatorAccountKey = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

        private readonly IntegrationDataStoreConfiguration _integrationDataStoreConfiguration;
        private readonly ILogger<AzureConnectionStringClientInitializerV2> _logger;

        public AzureConnectionStringClientInitializerV2(IOptions<IntegrationDataStoreConfiguration> integrationDataStoreConfiguration, ILogger<AzureConnectionStringClientInitializerV2> logger)
        {
            EnsureArg.IsNotNull(integrationDataStoreConfiguration?.Value, nameof(integrationDataStoreConfiguration));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _integrationDataStoreConfiguration = integrationDataStoreConfiguration.Value;
            _logger = logger;
        }

        public Task<BlobClient> GetAuthorizedBlobClientAsync(Uri blobUri)
        {
            return GetAuthorizedBlobClientAsync(blobUri, _integrationDataStoreConfiguration);
        }

        public Task<BlobClient> GetAuthorizedBlobClientAsync(Uri blobUri, IntegrationDataStoreConfiguration integrationDataStoreConfiguration)
        {
            EnsureArg.IsNotNull(blobUri, nameof(blobUri));
            return Task.FromResult(new BlobClient(blobUri, CreateSharedKeyCredential(integrationDataStoreConfiguration.StorageAccountConnection)));
        }

        public Task<BlockBlobClient> GetAuthorizedBlockBlobClientAsync(Uri blobUri)
        {
            return GetAuthorizedBlockBlobClientAsync(blobUri, _integrationDataStoreConfiguration);
        }

        public Task<BlockBlobClient> GetAuthorizedBlockBlobClientAsync(Uri blobUri, IntegrationDataStoreConfiguration integrationDataStoreConfiguration)
        {
            EnsureArg.IsNotNull(blobUri, nameof(blobUri));
            return Task.FromResult(new BlockBlobClient(blobUri, CreateSharedKeyCredential(integrationDataStoreConfiguration.StorageAccountConnection)));
        }

        public Task<BlobServiceClient> GetAuthorizedClientAsync()
        {
            return GetAuthorizedClientAsync(_integrationDataStoreConfiguration);
        }

        public Task<BlobServiceClient> GetAuthorizedClientAsync(IntegrationDataStoreConfiguration integrationDataStoreConfiguration)
        {
            if (string.IsNullOrWhiteSpace(integrationDataStoreConfiguration.StorageAccountConnection))
            {
                throw new IntegrationDataStoreClientInitializerException(Resources.InvalidConnectionSettings, HttpStatusCode.BadRequest);
            }

            BlobServiceClient blobClient = null;
            try
            {
                blobClient = new BlobServiceClient(integrationDataStoreConfiguration.StorageAccountConnection);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create a Cloud Blob Client");

                throw new IntegrationDataStoreClientInitializerException(Resources.InvalidConnectionSettings, HttpStatusCode.BadRequest);
            }

            return Task.FromResult(blobClient);
        }

        private static StorageSharedKeyCredential CreateSharedKeyCredential(string connectionString)
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
