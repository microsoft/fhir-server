// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Azure.Storage;
using Azure.Storage.Blobs;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Newtonsoft.Json;
using Xunit.Abstractions;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    internal static class ExportTestHelper
    {
        // Check export status and return output once we get 200
        internal static async Task<IList<Uri>> CheckExportStatus(TestFhirClient testFhirClient, Uri contentLocation, int timeToWaitInMinutes = 5)
        {
            HttpStatusCode resultCode = HttpStatusCode.Accepted;
            HttpResponseMessage response = null;
            int retryCount = 0;

            // Wait until status change or timeout
            while ((resultCode == HttpStatusCode.Accepted || resultCode == HttpStatusCode.ServiceUnavailable) && retryCount < 60)
            {
                await Task.Delay(timeToWaitInMinutes * 1000);

                response = await testFhirClient.CheckExportAsync(contentLocation);

                resultCode = response.StatusCode;

                retryCount++;
            }

            if (retryCount >= 60)
            {
                throw new Exception($"Export request timed out");
            }

            if (resultCode != HttpStatusCode.OK)
            {
                throw new Exception($"Export request failed with status code {resultCode}");
            }

            // we have got the result. Deserialize into output response.
            var contentString = await response.Content.ReadAsStringAsync();

            ExportJobResult exportJobResult = JsonConvert.DeserializeObject<ExportJobResult>(contentString);
            return exportJobResult.Output.Select(x => x.FileUri).ToList();
        }

        internal static async Task<Dictionary<(string resourceType, string resourceId), Resource>> GetResourcesFromFhirServer(
            TestFhirClient testFhirClient,
            Uri requestUri,
            FhirJsonParser fhirJsonParser,
            ITestOutputHelper outputHelper)
        {
            var resourceIdToResourceMapping = new Dictionary<(string resourceType, string resourceId), Resource>();

            while (requestUri != null)
            {
                HttpRequestMessage request = new HttpRequestMessage()
                {
                    RequestUri = requestUri,
                    Method = HttpMethod.Get,
                };

                using HttpResponseMessage response = await testFhirClient.HttpClient.SendAsync(request);

                var responseString = await response.Content.ReadAsStringAsync();
                Bundle searchResults;
                try
                {
                    searchResults = fhirJsonParser.Parse<Bundle>(responseString);
                }
                catch (Exception ex)
                {
                    outputHelper.WriteLine($"Unable to parse response into bundle: {ex}");
                    return resourceIdToResourceMapping;
                }

                foreach (Bundle.EntryComponent entry in searchResults.Entry)
                {
                    resourceIdToResourceMapping.TryAdd((entry.Resource.ResourceType.ToString(), entry.Resource.Id), entry.Resource);
                }

                // Look at whether a continuation token has been returned.
                string nextLink = searchResults.NextLink?.ToString();
                requestUri = nextLink == null ? null : new Uri(nextLink);
            }

            return resourceIdToResourceMapping;
        }

        internal static BlobServiceClient GetCloudStorageAccountHelper(string storageAccountName)
        {
            if (string.IsNullOrWhiteSpace(storageAccountName))
            {
                throw new Exception("StorageAccountName cannot be empty");
            }

            BlobServiceClient cloudAccount = null;

            // If we are running locally, then we need to connect to the Azure Storage Emulator.
            // Else we need to connect to a proper Azure Storage Account.
            if (storageAccountName.Equals("127", StringComparison.OrdinalIgnoreCase) || storageAccountName.Equals("devstoreaccount1", StringComparison.OrdinalIgnoreCase))
            {
                string emulatorConnectionString = "UseDevelopmentStorage=true";
                cloudAccount = new BlobServiceClient(emulatorConnectionString);
            }
            else
            {
                var storageAccountUri = new Uri($"https://{storageAccountName}.blob.core.windows.net");
                string storageSecret = Environment.GetEnvironmentVariable(storageAccountName + "_secret");
                string allAccounts = Environment.GetEnvironmentVariable("AllStorageAccounts");

                if (!string.IsNullOrWhiteSpace(storageSecret))
                {
                    var storageCredentials = new StorageSharedKeyCredential(storageAccountName, storageSecret);
                    cloudAccount = new BlobServiceClient(storageAccountUri, storageCredentials);
                }
                else if (!string.IsNullOrWhiteSpace(allAccounts))
                {
                    var splitAccounts = allAccounts.Split('|').ToList();
                    var nameIndex = splitAccounts.IndexOf(storageAccountName + "_secret");

                    if (nameIndex < 0)
                    {
                        throw new Exception("Unable to create a cloud storage account, key not provided.");
                    }

                    var storageCredentials = new StorageSharedKeyCredential(storageAccountName, splitAccounts[nameIndex + 1].Trim());
                    cloudAccount = new BlobServiceClient(storageAccountUri, storageCredentials);
                }
            }

            if (cloudAccount == null)
            {
                throw new Exception("Unable to create a cloud storage account");
            }

            return cloudAccount;
        }

        internal static async Task<Dictionary<(string resourceType, string resourceId), Resource>> DownloadBlobAndParse(
            IList<Uri> blobUri,
            FhirJsonParser fhirJsonParser,
            ITestOutputHelper outputHelper)
        {
            if (blobUri == null || blobUri.Count == 0)
            {
                return new Dictionary<(string resourceType, string resourceId), Resource>();
            }

            // Extract storage account name from blob uri in order to get corresponding access token.
            string storageAccountName = new BlobUriBuilder(blobUri[0]).AccountName;

            var blobClient = GetCloudStorageAccountHelper(storageAccountName);
            var resourceIdToResourceMapping = new Dictionary<(string resourceType, string resourceId), Resource>();

            foreach (Uri uri in blobUri)
            {
                var blobUriBuilder = new BlobUriBuilder(uri);
                var blobContainerClient = blobClient.GetBlobContainerClient(blobUriBuilder.BlobContainerName);
                var blob = blobContainerClient.GetBlobClient(blobUriBuilder.BlobName);

                using (var ms = new MemoryStream())
                using (var streamReader = new StreamReader(ms))
                {
                    await blob.DownloadToAsync(ms);
                    await ms.FlushAsync();
                    ms.Seek(0, SeekOrigin.Begin);

                    string entry;
                    while ((entry = await streamReader.ReadLineAsync()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(entry))
                        {
                            continue;
                        }

                        Resource resource;
                        try
                        {
                            resource = fhirJsonParser.Parse<Resource>(entry);
                        }
                        catch (Exception ex)
                        {
                            outputHelper.WriteLine($"Unable to parse ndjson string to resource: {ex}");
                            return resourceIdToResourceMapping;
                        }

                        // Ideally this should just be Add, but until we prevent duplicates from being added to the server
                        // there is a chance the same resource being added multiple times resulting in a key conflict.
                        resourceIdToResourceMapping.TryAdd((resource.ResourceType.ToString(), resource.Id), resource);
                    }
                }
            }

            return resourceIdToResourceMapping;
        }

        internal static bool ValidateDataFromBothSources(
            Dictionary<(string resourceType, string resourceId), Resource> dataFromServer,
            Dictionary<(string resourceType, string resourceId), Resource> dataFromStorageAccount,
            ITestOutputHelper outputHelper)
        {
            bool result = true;

            if (dataFromStorageAccount.Count != dataFromServer.Count)
            {
                outputHelper.WriteLine($"Count differs. Exported data count: {dataFromStorageAccount.Count} Fhir Server Count: {dataFromServer.Count}");
                result = false;

                foreach (KeyValuePair<(string resourceType, string resourceId), Resource> kvp in dataFromStorageAccount)
                {
                    if (!dataFromServer.ContainsKey(kvp.Key))
                    {
                        outputHelper.WriteLine($"Extra resource in exported data: {kvp.Key}");
                    }
                }
            }

            // Enable this check when creating/updating data validation tests to ensure there is data to export
            /*
            if (dataFromStorageAccount.Count == 0)
            {
                _outputHelper.WriteLine("No data exported. This test expects data to be present.");
                return false;
            }
            */

            int wrongCount = 0;
            foreach (KeyValuePair<(string resourceType, string resourceId), Resource> kvp in dataFromServer)
            {
                if (!dataFromStorageAccount.ContainsKey(kvp.Key))
                {
                    outputHelper.WriteLine($"Missing resource from exported data: {kvp.Key}");
                    result = false;
                    wrongCount++;
                    continue;
                }

                Resource exportEntry = dataFromStorageAccount[kvp.Key];
                Resource serverEntry = kvp.Value;
                if (!serverEntry.IsExactly(exportEntry))
                {
                    outputHelper.WriteLine($"Exported resource does not match server resource: {kvp.Key}");
                    result = false;
                    wrongCount++;
                    continue;
                }
            }

            outputHelper.WriteLine($"Missing or wrong match count: {wrongCount}");
            return result;
        }
    }
}
