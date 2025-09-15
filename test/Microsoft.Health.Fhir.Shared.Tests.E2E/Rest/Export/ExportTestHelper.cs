// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Azure.Storage;
using Azure.Storage.Blobs.Specialized;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Newtonsoft.Json;
using Xunit.Abstractions;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Export
{
    internal static class ExportTestHelper
    {
        // Check export status and return output once we get 200
        internal static async Task<IList<Uri>> CheckExportStatus(TestFhirClient testFhirClient, Uri contentLocation)
        {
            HttpStatusCode resultCode = HttpStatusCode.Accepted;
            HttpResponseMessage response = null;
            var sw = Stopwatch.StartNew();
            var maxSeconds = 300;

            // Wait until status change or timeout
            while ((resultCode == HttpStatusCode.Accepted || resultCode == HttpStatusCode.ServiceUnavailable) && sw.Elapsed.TotalSeconds < maxSeconds)
            {
                // dispose previous response.
                response?.Dispose();
                await Task.Delay(1000);
                response = await testFhirClient.CheckExportAsync(contentLocation);
                resultCode = response.StatusCode;
            }

            if (sw.Elapsed.TotalSeconds >= maxSeconds)
            {
                throw new Exception($"Export request timed out");
            }

            if (resultCode != HttpStatusCode.OK)
            {
                throw new Exception($"Export request failed with status code {resultCode}");
            }

            // we have got the result. Deserialize into output response.
            var contentString = await response.Content.ReadAsStringAsync();

            response.Dispose();
            ExportJobResult exportJobResult = JsonConvert.DeserializeObject<ExportJobResult>(contentString);
            return exportJobResult.Output.Select(x => x.FileUri).ToList();
        }

        internal static async Task<Dictionary<(string resourceType, string resourceId, string versionId), Resource>> GetResourcesFromFhirServer(
            TestFhirClient testFhirClient,
            Uri requestUri,
            FhirJsonParser fhirJsonParser,
            ITestOutputHelper outputHelper)
        {
            var resourceIdToResourceMapping = new Dictionary<(string resourceType, string resourceId, string versionId), Resource>();

            try
            {
                await foreach (Resource resource in GetResourceListFromFhirServer(testFhirClient, requestUri, fhirJsonParser))
                {
                    resourceIdToResourceMapping.TryAdd((resource.TypeName, resource.Id, resource.VersionId), resource);
                }
            }
            catch (Exception ex)
            {
                outputHelper.WriteLine($"Unable to parse response into bundle: {ex}");
            }

            return resourceIdToResourceMapping;
        }

        internal static async Task<Dictionary<(string resourceType, string resourceId, string versionId), Resource>> GetResourcesWithHistoryFromFhirServer(
            TestFhirClient testFhirClient,
            Uri requestUri,
            FhirJsonParser fhirJsonParser,
            ITestOutputHelper outputHelper)
        {
            var resourceIdToResourceMapping = new Dictionary<(string resourceType, string resourceId, string versionId), Resource>();

            try
            {
                await foreach (Resource resource in GetResourceListFromFhirServer(testFhirClient, requestUri, fhirJsonParser))
                {
                    string resourceWithHistoryUriString = $"{testFhirClient.HttpClient.BaseAddress}/{resource.TypeName}/{resource.Id}/_history";

                    if (requestUri.Query is not null)
                    {
                        resourceWithHistoryUriString += requestUri.Query;
                    }

                    await foreach (Resource historyResource in GetResourceListFromFhirServer(testFhirClient, new Uri(resourceWithHistoryUriString), fhirJsonParser))
                    {
                        resourceIdToResourceMapping.TryAdd((historyResource.TypeName, historyResource.Id, historyResource.VersionId), historyResource);
                    }
                }
            }
            catch (Exception ex)
            {
                outputHelper.WriteLine($"Unable to parse response into bundle: {ex}");
                return resourceIdToResourceMapping;
            }

            return resourceIdToResourceMapping;
        }

        private static async IAsyncEnumerable<Resource> GetResourceListFromFhirServer(
            TestFhirClient testFhirClient,
            Uri requestUri,
            FhirJsonParser fhirJsonParser)
        {
            while (requestUri != null)
            {
                HttpRequestMessage request = new HttpRequestMessage()
                {
                    RequestUri = requestUri,
                    Method = HttpMethod.Get,
                };

                using HttpResponseMessage response = await testFhirClient.HttpClient.SendAsync(request);

                var responseString = await response.Content.ReadAsStringAsync();
                Bundle searchResults = fhirJsonParser.Parse<Bundle>(responseString);

                // Look at whether a continuation token has been returned.
                string nextLink = searchResults.NextLink?.ToString();
                requestUri = nextLink == null ? null : new Uri(nextLink);

                foreach (Bundle.EntryComponent entry in searchResults.Entry)
                {
                    yield return entry.Resource;
                }
            }
        }

        internal static async Task<Dictionary<(string resourceType, string resourceId, string versionId), Resource>> DownloadBlobAndParse(
            IList<Uri> blobUri,
            FhirJsonParser fhirJsonParser,
            ITestOutputHelper outputHelper)
        {
            if (blobUri == null || blobUri.Count == 0)
            {
                return new Dictionary<(string resourceType, string resourceId, string versionId), Resource>();
            }

            // Extract storage account name from blob uri in order to get corresponding access token.
            Uri sampleUri = blobUri[0];

            var resourceIdToResourceMapping = new Dictionary<(string resourceType, string resourceId, string versionId), Resource>();

            foreach (Uri uri in blobUri)
            {
                var blob = AzureStorageBlobHelper.GetBlobClient(uri);
                var response = await blob.DownloadContentAsync();
                var allData = response.Value.Content.ToString();
                var splitData = allData.Split("\n");

                foreach (string entry in splitData)
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
                    resourceIdToResourceMapping.TryAdd((resource.TypeName, resource.Id, resource.VersionId), resource);
                }
            }

            return resourceIdToResourceMapping;
        }

        internal static bool ValidateDataFromBothSources(
            Dictionary<(string resourceType, string resourceId, string versionId), Resource> dataFromServer,
            Dictionary<(string resourceType, string resourceId, string versionId), Resource> dataFromStorageAccount,
            ITestOutputHelper outputHelper)
        {
            bool result = true;

            if (dataFromStorageAccount.Count != dataFromServer.Count)
            {
                outputHelper.WriteLine($"Count differs. Exported data count: {dataFromStorageAccount.Count} Fhir Server Count: {dataFromServer.Count}");
                result = false;

                foreach (KeyValuePair<(string resourceType, string resourceId, string versionId), Resource> kvp in dataFromStorageAccount)
                {
                    if (!dataFromServer.ContainsKey(kvp.Key))
                    {
                        outputHelper.WriteLine($"Extra resource in exported data: {kvp.Key}");
                    }
                }
            }

            // Enable this check when creating/updating data validation tests to ensure there is data to export
            // if (dataFromStorageAccount.Count == 0)
            // {
            //     outputHelper.WriteLine("No data exported. This test expects data to be present.");
            //     return false;
            // }

            int wrongCount = 0;
            foreach (KeyValuePair<(string resourceType, string resourceId, string versionId), Resource> kvp in dataFromServer)
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

            return result;
        }
    }
}
