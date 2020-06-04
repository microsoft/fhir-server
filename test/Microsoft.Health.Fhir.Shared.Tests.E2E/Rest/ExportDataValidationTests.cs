// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Auth;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [Trait(Traits.Category, Categories.ExportDataValidation)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class ExportDataValidationTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private readonly TestFhirClient _testFhirClient;
        private readonly ITestOutputHelper _outputHelper;

        public ExportDataValidationTests(HttpIntegrationTestFixture fixture, ITestOutputHelper testOutputHelper)
        {
            _testFhirClient = fixture.TestFhirClient;
            _outputHelper = testOutputHelper;
        }

        [Fact(Skip = "yes please")]
        public async Task GivenFhirServer_WhenDataIsExported_ThenExportedDataIsSameAsDataInFhirServer()
        {
            // Trigger export request and check for export status
            Uri contentLocation = await _testFhirClient.ExportAsync();
            IList<Uri> blobUris = await CheckExportStatus(contentLocation);

            // Download exported data from storage account
            Dictionary<string, string> dataFromExport = await DownloadBlobAndParse(blobUris);

            // Download all resources from fhir server
            Dictionary<string, string> dataFromFhirServer = await GetResourcesFromFhirServer(_testFhirClient.HttpClient.BaseAddress);

            // Assert both data are equal
            Assert.True(ValidateDataFromBothSources(dataFromFhirServer, dataFromExport));
        }

        private bool ValidateDataFromBothSources(Dictionary<string, string> dataFromServer, Dictionary<string, string> dataFromStorageAccount)
        {
            bool result = true;
            if (dataFromStorageAccount.Count != dataFromServer.Count)
            {
                _outputHelper.WriteLine($"Count differs. Exported data count: {dataFromStorageAccount.Count} Fhir Server Count: {dataFromServer.Count}");
                return false;
            }

            int wrongCount = 0;
            foreach (KeyValuePair<string, string> kvp in dataFromServer)
            {
                if (!dataFromStorageAccount.ContainsKey(kvp.Key))
                {
                    _outputHelper.WriteLine($"Missing resource from exported data: {kvp.Key}");
                    result = false;
                    wrongCount++;
                    continue;
                }

                string exportEntry = dataFromStorageAccount[kvp.Key];
                string serverEntry = kvp.Value;
                if (serverEntry.Equals(exportEntry))
                {
                    _outputHelper.WriteLine($"Exported resource does not match server resource: {kvp.Key}");
                    result = false;
                    wrongCount++;
                    continue;
                }
            }

            _outputHelper.WriteLine($"Missing or wrong match count: {wrongCount}");
            return result;
        }

        // Check export status and return output once we get 200
        private async Task<IList<Uri>> CheckExportStatus(Uri contentLocation)
        {
            HttpStatusCode resultCode = HttpStatusCode.Accepted;
            HttpResponseMessage response = null;
            while (resultCode == HttpStatusCode.Accepted)
            {
                await Task.Delay(5000);

                response = await _testFhirClient.CheckExportAsync(contentLocation);

                resultCode = response.StatusCode;
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

        private async Task<Dictionary<string, string>> DownloadBlobAndParse(IList<Uri> blobUri)
        {
            if (blobUri == null || blobUri.Count == 0)
            {
                return new Dictionary<string, string>();
            }

            // Extract storage account name from blob uri in order to get corresponding access token.
            Uri sampleUri = blobUri[0];
            string storageAccountName = sampleUri.Host.Split('.')[0];

            CloudStorageAccount cloudAccount = GetCloudStorageAccountHelper(storageAccountName);
            CloudBlobClient blobClient = cloudAccount.CreateCloudBlobClient();
            Dictionary<string, string> resourceIdToResourceMapping = new Dictionary<string, string>();

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

                    JObject resource = JObject.Parse(entry);
                    resourceIdToResourceMapping.Add(resource["id"].ToString(), entry);
                }
            }

            // Delete the container since we have downloaded the data.
            Regex regex = new Regex(@"(?<guid>[a-f0-9]{8}(?:\-[a-f0-9]{4}){3}\-[a-f0-9]{12})");
            var guidMatch = regex.Match(blobUri[0].ToString());
            CloudBlobContainer container = blobClient.GetContainerReference(guidMatch.Value);
            await container.DeleteIfExistsAsync();

            return resourceIdToResourceMapping;
        }

        private async Task<Dictionary<string, string>> GetResourcesFromFhirServer(Uri requestUri)
        {
            Dictionary<string, string> resourceIdToResourceMapping = new Dictionary<string, string>();

            while (requestUri != null)
            {
                HttpRequestMessage request = new HttpRequestMessage()
                {
                    RequestUri = requestUri,
                    Method = HttpMethod.Get,
                };

                using HttpResponseMessage response = await _testFhirClient.HttpClient.SendAsync(request);

                var responseString = await response.Content.ReadAsStringAsync();

                JObject result = JObject.Parse(responseString);

                JArray entries = (JArray)result["entry"];
                foreach (JToken entry in entries)
                {
                    string id = entry["resource"]["id"].ToString();
                    string resource = entry["resource"].ToString().Trim();

                    resourceIdToResourceMapping.Add(id, resource);
                }

                // Look at whether a continutation token has been returned.
                // We will always have self link. We are looking for the "next" link
                JArray links = (JArray)result["link"];
                string nextUri = null;
                if (links != null && links.Count > 1)
                {
                    foreach (JToken link in links)
                    {
                        if (link["relation"].ToString() == "next")
                        {
                            nextUri = link["url"].ToString();
                            break;
                        }
                    }
                }

                requestUri = nextUri == null ? null : new Uri(nextUri);
            }

            return resourceIdToResourceMapping;
        }

        private CloudStorageAccount GetCloudStorageAccountHelper(string storageAccountName)
        {
            if (string.IsNullOrWhiteSpace(storageAccountName))
            {
                throw new Exception("StorageAccountName cannot be empty");
            }

            CloudStorageAccount cloudAccount = null;

            // If we are running locally, then we need to connect to the Azure Storage Emulator.
            // Else we need to connect to a proper Azure Storage Account.
            if (storageAccountName.Equals("127", StringComparison.OrdinalIgnoreCase))
            {
                string emulatorConnectionString = "UseDevelopmentStorage=true";
                CloudStorageAccount.TryParse(emulatorConnectionString, out cloudAccount);
            }
            else
            {
                string storageSecret = Environment.GetEnvironmentVariable(storageAccountName + "_secret");
                if (!string.IsNullOrWhiteSpace(storageSecret))
                {
                    StorageCredentials storageCredentials = new StorageCredentials(storageAccountName, storageSecret);
                    cloudAccount = new CloudStorageAccount(storageCredentials, useHttps: true);
                }
            }

            if (cloudAccount == null)
            {
                throw new Exception("Unable to create a cloud storage account");
            }

            return cloudAccount;
        }
    }
}
