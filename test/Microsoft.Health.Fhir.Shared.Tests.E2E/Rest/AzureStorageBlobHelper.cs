// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Azure.Identity;
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

        public static BlobClient GetBlobClient(Uri storageServiceUri, string blobContainerName, string blobName)
        {
            // Create the BlobServiceClient from the connection string
            BlobServiceClient blobServiceClient = GetBlobServiceClient(storageServiceUri);

            // Get a reference to the container
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(blobContainerName);

            // Get a reference to the blob
            BlobClient blobClient = containerClient.GetBlobClient(blobName);

            return blobClient;
        }

        public static BlobClient GetBlobClient(Uri blobUri)
        {
            // Parse the blobUri to extract storage account name, container name, and blob name
            string storageAccountName = blobUri.Host.Split('.')[0];
            string[] segments = blobUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length < 2)
            {
                throw new Exception("Invalid blob URI");
            }

            string blobContainerName = segments[0];
            string blobName = string.Join("/", segments.Skip(1));

            // Construct the storage service URI
            Uri storageServiceUri = new Uri($"https://{storageAccountName}.blob.core.windows.net");

            // Use the other GetBlobClient method
            return GetBlobClient(storageServiceUri, blobContainerName, blobName);
        }

        public static BlobServiceClient GetBlobServiceClient(Uri storageServiceUri)
        {
            if (IsLocalRun(storageServiceUri))
            {
                return new BlobServiceClient(StorageEmulatorConnectionString);
            }

            var credential = new DefaultAzureCredential();
            var blobServiceClient = new BlobServiceClient(storageServiceUri, credential);
            return blobServiceClient;
        }

        public static BlobContainerClient GetBlobContainerClient(Uri storageServiceUri, string blobContainerName)
        {
            // Create the BlobServiceClient and BlobContainerClient once
            BlobServiceClient blobServiceClient = GetBlobServiceClient(storageServiceUri);
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(blobContainerName);
            return containerClient;
        }

        private static bool IsLocalRun(Uri uri)
        {
            EnsureArg.IsNotNull(uri, nameof(uri));

            return uri.Host.Split('.')[0].Equals("127", StringComparison.OrdinalIgnoreCase);
        }
    }
}
