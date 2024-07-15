// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Azure.Core;
using Azure.Identity;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using EnsureThat;
using IdentityServer4.Models;

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

            TokenCredential credential = IsAzurePipelinesRun()
                ? new AzurePipelinesCredential(
                    Environment.GetEnvironmentVariable("AZURESUBSCRIPTION_TENANT_ID"),
                    Environment.GetEnvironmentVariable("AZURESUBSCRIPTION_CLIENT_ID"),
                    Environment.GetEnvironmentVariable("AZURESUBSCRIPTION_SERVICE_CONNECTION_ID"),
                    Environment.GetEnvironmentVariable("SYSTEM_ACCESSTOKEN"))
                : new DefaultAzureCredential();

            var blobServiceClient = new BlobServiceClient(storageServiceUri, credential);
            return blobServiceClient;
        }

        public static BlobContainerClient GetBlobContainerClient(Uri storageServiceUri, string blobContainerName)
        {
            BlobServiceClient blobServiceClient = GetBlobServiceClient(storageServiceUri);
            BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(blobContainerName);
            return containerClient;
        }

        private static bool IsLocalRun(Uri uri)
        {
            EnsureArg.IsNotNull(uri, nameof(uri));

            return uri.Host.Split('.')[0].Equals("127", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAzurePipelinesRun()
        {
            string[] variableNames = [
                "AZURESUBSCRIPTION_CLIENT_ID",
                "AZURESUBSCRIPTION_TENANT_ID",
                "AZURESUBSCRIPTION_SERVICE_CONNECTION_ID",
                "SYSTEM_ACCESSTOKEN",
            ];

            foreach (var variableName in variableNames)
            {
                string variableValue = Environment.GetEnvironmentVariable(variableName);
                if (string.IsNullOrEmpty(variableValue))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
