// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Auth;
using Microsoft.Azure.Storage.Blob;

namespace Microsoft.Health.Fhir.Tests.E2E
{
    internal static class StorageAccountHelper
    {
        private const string LocalIntegrationStoreConnectionString = "UseDevelopmentStorage=true";

        public static async Task<CloudStorageAccount> GetCloudStorageAccountAsync(CancellationToken cancellationToken)
        {
            CloudStorageAccount storageAccount = null;

            string exportStoreFromEnvironmentVariable = Environment.GetEnvironmentVariable("TestExportStoreUri");
            string exportStoreKeyFromEnvironmentVariable = Environment.GetEnvironmentVariable("TestExportStoreKey");
            if (!string.IsNullOrEmpty(exportStoreFromEnvironmentVariable) && !string.IsNullOrEmpty(exportStoreKeyFromEnvironmentVariable))
            {
                Uri integrationStoreUri = new Uri(exportStoreFromEnvironmentVariable);
                string storageAccountName = integrationStoreUri.Host.Split('.')[0];
                StorageCredentials storageCredentials = new StorageCredentials(storageAccountName, exportStoreKeyFromEnvironmentVariable);
                storageAccount = new CloudStorageAccount(storageCredentials, useHttps: true);
            }
            else
            {
                CloudStorageAccount.TryParse(LocalIntegrationStoreConnectionString, out storageAccount);
            }

            if (storageAccount == null)
            {
                throw new InvalidOperationException($"Unable to create a new instance of {nameof(CloudStorageAccount)}. ");
            }

            int attempt = 0;
            const int maxAttempt = 3;
            do
            {
                try
                {
                    CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
                    await blobClient.GetAccountPropertiesAsync();

                    break;
                }
                catch (StorageException se) when (IsAuthenticationFailed(se))
                {
                    // 'StorageException: Microsoft.Azure.Storage.StorageException: Server failed to authenticate the request. Make sure the value of Authorization header is formed correctly including the signature.'
                    // There is a know issue in Storage Account where an exception is raised if the clock of the client and the server do not match.
                    // The following link documents the scenario: https://docs.microsoft.com/en-us/archive/blogs/kwill/http-403-server-failed-to-authenticate-the-request-when-using-shared-access-signatures
                    // And it's mentioned that:
                    //        "(...) the client machine’s system time is at least 2 seconds faster than the system time for the
                    //         front end authentication server handling that particular Azure Storage request.
                    //         And the Azure Storage authentication server is going to reject this request (...)".

                    if (attempt == maxAttempt)
                    {
                        throw new InvalidOperationException(
                            $"Unable to stablish a connection with the Storage Account '{storageAccount.BlobStorageUri}' after {maxAttempt} attempts. Reason: {se.Message.ToString()}.",
                            se);
                    }

                    await Task.Delay(2000);

                    attempt++;
                }
            }
            while (true);

            return storageAccount;
        }

        public static async Task<IEnumerable<string>> DownloadBlobAndParseAsync(IList<Uri> blobUri, CancellationToken cancellationToken)
        {
            CloudStorageAccount cloudAccount = await GetCloudStorageAccountAsync(cancellationToken);
            CloudBlobClient blobClient = cloudAccount.CreateCloudBlobClient();
            var result = new List<string>();

            foreach (Uri uri in blobUri)
            {
                var blob = new CloudBlockBlob(uri, blobClient);
                string allData = await blob.DownloadTextAsync();

                var splitData = allData.Split("\n");

                foreach (var entry in splitData)
                {
                    if (string.IsNullOrWhiteSpace(entry))
                    {
                        continue;
                    }

                    result.Add(entry);
                }
            }

            return result;
        }

        public static bool IsAuthenticationFailed(StorageException se)
        {
            return se.RequestInformation.HttpStatusCode == (int)HttpStatusCode.Forbidden || se.RequestInformation.HttpStatusCode == (int)HttpStatusCode.Unauthorized;
        }
    }
}
