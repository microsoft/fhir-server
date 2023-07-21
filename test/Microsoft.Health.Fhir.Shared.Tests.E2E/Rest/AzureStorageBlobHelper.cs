// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using EnsureThat;

namespace Microsoft.Health.Fhir.Tests.E2E
{
    internal static class AzureStorageBlobHelper
    {
        // Well-know storage emulator account info, not to be used in production (see https://learn.microsoft.com/en-us/azure/storage/common/storage-configure-connection-string?toc=%2Fazure%2Fstorage%2Fblobs%2Ftoc.json&bc=%2Fazure%2Fstorage%2Fblobs%2Fbreadcrumb%2Ftoc.json#configure-a-connection-string-for-azurite)
        public const string StorageEmulatorConnectionString = "UseDevelopmentStorage=true";
        private const string StorageEmulatorConnectionStringPrefix = "UseDevelopmentStorage";
        private const string StorageEmulatorAccountName = "devstoreaccount1";
        private const string StorageEmulatorAccountKey = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

        public static BlobServiceClient CreateBlobServiceClient(string storageUriEnvironmentName, string storageKeyEnvironmentName)
        {
            (Uri storageUri, StorageSharedKeyCredential credential, string connectionString) = AzureStorageBlobHelper.GetStorageCredentialsFromEnvironmentVariables(
                storageUriEnvironmentName,
                storageKeyEnvironmentName);
            return CreateBlobServiceClient(storageUri, credential, connectionString);
        }

        public static BlobServiceClient CreateBlobServiceClient(Uri storageServiceUri, StorageSharedKeyCredential credential, string connectionString)
        {
            if (storageServiceUri != null && !IsLocalRun(storageServiceUri))
            {
                return new BlobServiceClient(storageServiceUri, credential);
            }

            return new BlobServiceClient(connectionString);
        }

        public static BlobClient CreateBlobClient(Uri blobUri, StorageSharedKeyCredential credential, string connectionString)
        {
            EnsureArg.IsNotNull(blobUri, nameof(blobUri));

            if (!IsLocalRun(blobUri))
            {
                return new BlobClient(blobUri, credential);
            }

            return new BlobClient(blobUri, GetSharedKeyCredential(StorageEmulatorConnectionString));
        }

        public static BlockBlobClient CreateBlockBlobClient(Uri blobUri, StorageSharedKeyCredential credential, string connectionString)
        {
            EnsureArg.IsNotNull(blobUri, nameof(blobUri));

            if (!IsLocalRun(blobUri))
            {
                return new BlockBlobClient(blobUri, credential);
            }

            return new BlockBlobClient(blobUri, GetSharedKeyCredential(StorageEmulatorConnectionString));
        }

        public static (Uri storageServiceUri, StorageSharedKeyCredential credential, string connectionString) GetStorageCredentialsFromEnvironmentVariables(
            string storageUriEnvironmentName,
            string storageKeyEnvironmentName)
        {
            Uri serviceUri = null;
            StorageSharedKeyCredential storageSharedKeyCredential = null;

            string storageUriFromEnvironmentVariable = Environment.GetEnvironmentVariable(storageUriEnvironmentName);
            string storageKeyFromEnvironmentVariable = Environment.GetEnvironmentVariable(storageKeyEnvironmentName);
            if (!string.IsNullOrEmpty(storageUriFromEnvironmentVariable) && !string.IsNullOrEmpty(storageKeyFromEnvironmentVariable))
            {
                try
                {
                    serviceUri = new Uri(storageUriFromEnvironmentVariable);
                    storageSharedKeyCredential = new StorageSharedKeyCredential(serviceUri.Host.Split('.')[0], storageKeyFromEnvironmentVariable);
                }
                catch (Exception ex)
                {
                    throw new Exception(
                        $"Unable to create a storage shared key credential. {storageUriFromEnvironmentVariable}",
                        ex);
                }
            }

            return (serviceUri, storageSharedKeyCredential, StorageEmulatorConnectionString);
        }

        private static bool IsLocalRun(Uri uri)
        {
            EnsureArg.IsNotNull(uri, nameof(uri));

            return uri.Host.Split('.')[0].Equals("127", StringComparison.OrdinalIgnoreCase);
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
