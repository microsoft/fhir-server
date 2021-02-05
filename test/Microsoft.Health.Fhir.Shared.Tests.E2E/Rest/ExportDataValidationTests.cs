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
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Auth;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Shared.Tests.E2E.Rest;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using Xunit;
using Xunit.Abstractions;
using FhirGroup = Hl7.Fhir.Model.Group;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [Trait(Traits.Category, Categories.ExportDataValidation)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class ExportDataValidationTests : IClassFixture<ExportTestFixture>
    {
        private readonly TestFhirClient _testFhirClient;
        private readonly ITestOutputHelper _outputHelper;
        private readonly FhirJsonParser _fhirJsonParser;
        private readonly ExportTestFixture _fixture;

        public ExportDataValidationTests(ExportTestFixture fixture, ITestOutputHelper testOutputHelper)
        {
            _testFhirClient = fixture.TestFhirClient;
            _outputHelper = testOutputHelper;
            _fhirJsonParser = new FhirJsonParser();
            _fixture = fixture;
        }

        [Fact(Skip = "Failing CI build")]
        public async Task GivenFhirServer_WhenAllDataIsExported_ThenExportedDataIsSameAsDataInFhirServer()
        {
            // NOTE: Azure Storage Emulator is required to run these tests locally.

            // Trigger export request and check for export status
            Uri contentLocation = await _testFhirClient.ExportAsync();
            IList<Uri> blobUris = await CheckExportStatus(contentLocation);

            // Download exported data from storage account
            Dictionary<(string resourceType, string resourceId), Resource> dataFromExport = await DownloadBlobAndParse(blobUris);

            // Download all resources from fhir server
            Dictionary<(string resourceType, string resourceId), Resource> dataFromFhirServer = await GetResourcesFromFhirServer(_testFhirClient.HttpClient.BaseAddress);

            // Assert both data are equal
            Assert.True(ValidateDataFromBothSources(dataFromFhirServer, dataFromExport));
        }

        [Fact(Skip = "Failing CI build")]
        public async Task GivenFhirServer_WhenPatientDataIsExported_ThenExportedDataIsSameAsDataInFhirServer()
        {
            // NOTE: Azure Storage Emulator is required to run these tests locally.

            // Trigger export request and check for export status
            Uri contentLocation = await _testFhirClient.ExportAsync("Patient/");
            IList<Uri> blobUris = await CheckExportStatus(contentLocation);

            // Download exported data from storage account
            Dictionary<(string resourceType, string resourceId), Resource> dataFromExport = await DownloadBlobAndParse(blobUris);

            // Download resources from fhir server
            Uri address = new Uri(_testFhirClient.HttpClient.BaseAddress, "Patient/");
            Dictionary<(string resourceType, string resourceId), Resource> dataFromFhirServer = await GetResourcesFromFhirServer(address);

            Dictionary<(string resourceType, string resourceId), Resource> compartmentData = new Dictionary<(string resourceType, string resourceId), Resource>();
            foreach ((string resourceType, string resourceId) key in dataFromFhirServer.Keys)
            {
                address = new Uri(_testFhirClient.HttpClient.BaseAddress, "Patient/" + key.resourceId + "/*");

                // copies all the new values into the compartment data dictionary
                (await GetResourcesFromFhirServer(address)).ToList().ForEach(x => compartmentData.TryAdd(x.Key, x.Value));
            }

            compartmentData.ToList().ForEach(x => dataFromFhirServer.TryAdd(x.Key, x.Value));
            dataFromFhirServer.Union(compartmentData);

            // Assert both data are equal
            Assert.True(ValidateDataFromBothSources(dataFromFhirServer, dataFromExport));
        }

        [Fact(Skip = "Failing CI build")]
        public async Task GivenFhirServer_WhenAllObservationAndPatientDataIsExported_ThenExportedDataIsSameAsDataInFhirServer()
        {
            // NOTE: Azure Storage Emulator is required to run these tests locally.

            // Trigger export request and check for export status
            Uri contentLocation = await _testFhirClient.ExportAsync(string.Empty, "_type=Observation,Patient");
            IList<Uri> blobUris = await CheckExportStatus(contentLocation);

            // Download exported data from storage account
            Dictionary<(string resourceType, string resourceId), Resource> dataFromExport = await DownloadBlobAndParse(blobUris);

            // Download resources from fhir server
            Uri address = new Uri(_testFhirClient.HttpClient.BaseAddress, "?_type=Observation,Patient");
            Dictionary<(string resourceType, string resourceId), Resource> dataFromFhirServer = await GetResourcesFromFhirServer(address);

            // Assert both data are equal
            Assert.True(ValidateDataFromBothSources(dataFromFhirServer, dataFromExport));
        }

        [Fact(Skip = "Failing CI build")]
        public async Task GivenFhirServer_WhenPatientObservationDataIsExported_ThenExportedDataIsSameAsDataInFhirServer()
        {
            // NOTE: Azure Storage Emulator is required to run these tests locally.

            // Trigger export request and check for export status
            Uri contentLocation = await _testFhirClient.ExportAsync("Patient/", "_type=Observation");
            IList<Uri> blobUris = await CheckExportStatus(contentLocation);

            // Download exported data from storage account
            Dictionary<(string resourceType, string resourceId), Resource> dataFromExport = await DownloadBlobAndParse(blobUris);

            // Download resources from fhir server
            Uri address = new Uri(_testFhirClient.HttpClient.BaseAddress, "Patient/");
            Dictionary<(string resourceType, string resourceId), Resource> patientData = await GetResourcesFromFhirServer(address);

            Dictionary<(string resourceType, string resourceId), Resource> compartmentData = new Dictionary<(string resourceType, string resourceId), Resource>();
            foreach ((string resourceType, string resourceId) key in patientData.Keys)
            {
                address = new Uri(_testFhirClient.HttpClient.BaseAddress, "Patient/" + key.resourceId + "/Observation");

                // copies all the new values into the compartment data dictionary
                (await GetResourcesFromFhirServer(address)).ToList().ForEach(x => compartmentData.TryAdd(x.Key, x.Value));
            }

            compartmentData.ToList().ForEach(x => patientData.TryAdd(x.Key, x.Value));
            patientData.Union(compartmentData);

            // Assert both data are equal
            Assert.True(ValidateDataFromBothSources(compartmentData, dataFromExport));
        }

        [Fact]
        public async Task GivenFhirServer_WhenGroupDataIsExported_ThenExportedDataIsSameAsDataInFhirServer()
        {
            // NOTE: Azure Storage Emulator is required to run these tests locally.

            // Add data for test
            var (dataInFhirServer, groupId) = await CreateGroupWithPatient(true);

            // Trigger export request and check for export status
            Uri contentLocation = await _testFhirClient.ExportAsync($"Group/{groupId}/");
            IList<Uri> blobUris = await CheckExportStatus(contentLocation);

            // Download exported data from storage account
            Dictionary<(string resourceType, string resourceId), Resource> dataFromExport = await DownloadBlobAndParse(blobUris);

            // Assert both sets of data are equal
            Assert.True(ValidateDataFromBothSources(dataInFhirServer, dataFromExport));
        }

        [Fact]
        public async Task GivenFhirServer_WhenGroupDataIsExportedWithTypeParameter_ThenExportedDataIsSameAsDataInFhirServer()
        {
            // NOTE: Azure Storage Emulator is required to run these tests locally.

            // Add data for test
            var (dataInFhirServer, groupId) = await CreateGroupWithPatient(false);

            // Trigger export request and check for export status
            Uri contentLocation = await _testFhirClient.ExportAsync($"Group/{groupId}/", "_type=RelatedPerson,Encounter");
            IList<Uri> blobUris = await CheckExportStatus(contentLocation);

            // Download exported data from storage account
            Dictionary<(string resourceType, string resourceId), Resource> dataFromExport = await DownloadBlobAndParse(blobUris);

            // Assert both sets of data are equal
            Assert.True(ValidateDataFromBothSources(dataInFhirServer, dataFromExport));
        }

        [Fact]
        public async Task GivenFhirServer_WhenGroupDataWithNoMemberPatientIdIsExported_ThenNoDataIsExported()
        {
            // NOTE: Azure Storage Emulator is required to run these tests locally.

            // Add data for test
            string groupId = await CreateGroupWithoutPatientIds();

            // Trigger export request and check for export status
            Uri contentLocation = await _testFhirClient.ExportAsync($"Group/{groupId}/");
            IList<Uri> blobUris = await CheckExportStatus(contentLocation);

            Assert.Empty(blobUris);

            async Task<string> CreateGroupWithoutPatientIds()
            {
                var group = new FhirGroup()
                {
                    Type = FhirGroup.GroupType.Person,
                    Actual = true,
                };

                var groupResponse = await _testFhirClient.CreateAsync(group);
                return groupResponse.Resource.Id;
            }
        }

        [Fact(Skip = "Failing CI build")]
        public async Task GivenFhirServer_WhenAllDataIsExportedToASpecificContainer_ThenExportedDataIsInTheSpecifiedContianer()
        {
            // NOTE: Azure Storage Emulator is required to run these tests locally.

            string testContainer = "test-container";

            // Trigger export request and check for export status
            Uri contentLocation = await _testFhirClient.ExportAsync(parameters: $"_container={testContainer}");
            IList<Uri> blobUris = await CheckExportStatus(contentLocation);

            // Download exported data from storage account
            Dictionary<(string resourceType, string resourceId), Resource> dataFromExport = await DownloadBlobAndParse(blobUris);

            // Download all resources from fhir server
            Dictionary<(string resourceType, string resourceId), Resource> dataFromFhirServer = await GetResourcesFromFhirServer(_testFhirClient.HttpClient.BaseAddress);

            // Assert both data are equal
            Assert.True(ValidateDataFromBothSources(dataFromFhirServer, dataFromExport));
            Assert.True(blobUris.All((url) => url.OriginalString.Contains(testContainer)));
        }

        [Fact]
        public async Task GivenFhirServer_WhenDataIsExported_ThenExportTaskMetricsNotificationShouldBePosted()
        {
            // NOTE: Azure Storage Emulator is required to run these tests locally.

            if (!_fixture.IsUsingInProcTestServer)
            {
                // This test only works with the in-proc server with a customized metric handler.
                return;
            }

            // Clean notification before tests
            _fixture.MetricHandler.ResetCount();

            // Add data for test
            var (_, groupId) = await CreateGroupWithPatient(true);

            // Trigger export request and check for export status
            Uri contentLocation = await _testFhirClient.ExportAsync($"Group/{groupId}/");
            await CheckExportStatus(contentLocation);

            // Assert at least one notification handled.
            Assert.Single(_fixture.MetricHandler.NotificationMapping[typeof(ExportTaskMetricsNotification)]);
        }

        private bool ValidateDataFromBothSources(Dictionary<(string resourceType, string resourceId), Resource> dataFromServer, Dictionary<(string resourceType, string resourceId), Resource> dataFromStorageAccount)
        {
            bool result = true;

            if (dataFromStorageAccount.Count != dataFromServer.Count)
            {
                _outputHelper.WriteLine($"Count differs. Exported data count: {dataFromStorageAccount.Count} Fhir Server Count: {dataFromServer.Count}");
                result = false;

                foreach (KeyValuePair<(string resourceType, string resourceId), Resource> kvp in dataFromStorageAccount)
                {
                    if (!dataFromServer.ContainsKey(kvp.Key))
                    {
                        _outputHelper.WriteLine($"Extra resource in exported data: {kvp.Key}");
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
                    _outputHelper.WriteLine($"Missing resource from exported data: {kvp.Key}");
                    result = false;
                    wrongCount++;
                    continue;
                }

                Resource exportEntry = dataFromStorageAccount[kvp.Key];
                Resource serverEntry = kvp.Value;
                if (!serverEntry.IsExactly(exportEntry))
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
            int retryCount = 0;

            // Wait until status change or 5 minutes
            while (resultCode == HttpStatusCode.Accepted && retryCount < 60)
            {
                await Task.Delay(5000);

                response = await _testFhirClient.CheckExportAsync(contentLocation);

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

        private async Task<Dictionary<(string resourceType, string resourceId), Resource>> DownloadBlobAndParse(IList<Uri> blobUri)
        {
            if (blobUri == null || blobUri.Count == 0)
            {
                return new Dictionary<(string resourceType, string resourceId), Resource>();
            }

            // Extract storage account name from blob uri in order to get corresponding access token.
            Uri sampleUri = blobUri[0];
            string storageAccountName = sampleUri.Host.Split('.')[0];

            CloudStorageAccount cloudAccount = GetCloudStorageAccountHelper(storageAccountName);
            CloudBlobClient blobClient = cloudAccount.CreateCloudBlobClient();
            var resourceIdToResourceMapping = new Dictionary<(string resourceType, string resourceId), Resource>();

            foreach (Uri uri in blobUri)
            {
                var blob = new CloudBlockBlob(uri, blobClient);
                string allData = await blob.DownloadTextAsync();

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
                        resource = _fhirJsonParser.Parse<Resource>(entry);
                    }
                    catch (Exception ex)
                    {
                        _outputHelper.WriteLine($"Unable to parse ndjson string to resource: {ex}");
                        return resourceIdToResourceMapping;
                    }

                    // Ideally this should just be Add, but until we prevent duplicates from being added to the server
                    // there is a chance the same resource being added multiple times resulting in a key conflict.
                    resourceIdToResourceMapping.TryAdd((resource.ResourceType.ToString(), resource.Id), resource);
                }
            }

            // Delete the container since we have downloaded the data.
            Regex regex = new Regex(@"(?<guid>[a-f0-9]{8}(?:\-[a-f0-9]{4}){3}\-[a-f0-9]{12})");
            var guidMatch = regex.Match(blobUri[0].ToString());
            CloudBlobContainer container = blobClient.GetContainerReference(guidMatch.Value);
            await container.DeleteIfExistsAsync();

            return resourceIdToResourceMapping;
        }

        private async Task<Dictionary<(string resourceType, string resourceId), Resource>> GetResourcesFromFhirServer(Uri requestUri)
        {
            var resourceIdToResourceMapping = new Dictionary<(string resourceType, string resourceId), Resource>();

            while (requestUri != null)
            {
                HttpRequestMessage request = new HttpRequestMessage()
                {
                    RequestUri = requestUri,
                    Method = HttpMethod.Get,
                };

                using HttpResponseMessage response = await _testFhirClient.HttpClient.SendAsync(request);

                var responseString = await response.Content.ReadAsStringAsync();
                Bundle searchResults;
                try
                {
                    searchResults = _fhirJsonParser.Parse<Bundle>(responseString);
                }
                catch (Exception ex)
                {
                    _outputHelper.WriteLine($"Unable to parse response into bundle: {ex}");
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

        private async Task<(Dictionary<(string resourceType, string resourceId), Resource> serverData, string groupId)> CreateGroupWithPatient(bool includeAllResources)
        {
            // Add data for test
            var patient = new Patient();
            var patientResponse = await _testFhirClient.CreateAsync(patient);
            var patientId = patientResponse.Resource.Id;

            var relative = new RelatedPerson()
            {
                Patient = new ResourceReference($"{KnownResourceTypes.Patient}/{patientId}"),
            };

            var relativeResponse = await _testFhirClient.CreateAsync(relative);
            var relativeId = relativeResponse.Resource.Id;

            var encounter = new Encounter()
            {
                Status = Encounter.EncounterStatus.InProgress,
                Class = new Coding()
                {
                    Code = "test",
                },
                Subject = new ResourceReference($"{KnownResourceTypes.Patient}/{patientId}"),
            };

            var encounterResponse = await _testFhirClient.CreateAsync(encounter);
            var encounterId = encounterResponse.Resource.Id;

            var observation = new Observation()
            {
                Status = ObservationStatus.Final,
                Code = new CodeableConcept()
                {
                    Coding = new List<Coding>()
                    {
                        new Coding()
                        {
                            Code = "test",
                        },
                    },
                },
                Subject = new ResourceReference($"{KnownResourceTypes.Patient}/{patientId}"),
            };

            var observationResponse = await _testFhirClient.CreateAsync(observation);
            var observationId = observationResponse.Resource.Id;

            var group = new FhirGroup()
            {
                Type = FhirGroup.GroupType.Person,
                Actual = true,
                Member = new List<FhirGroup.MemberComponent>()
                {
                    new FhirGroup.MemberComponent()
                    {
                        Entity = new ResourceReference($"{KnownResourceTypes.Patient}/{patientId}"),
                    },
                },
            };

            var groupResponse = await _testFhirClient.CreateAsync(group);
            var groupId = groupResponse.Resource.Id;

            var resourceDictionary = new Dictionary<(string resourceType, string resourceId), Resource>();
            resourceDictionary.Add((KnownResourceTypes.RelatedPerson, relativeId), relativeResponse.Resource);
            resourceDictionary.Add((KnownResourceTypes.Encounter, encounterId), encounterResponse.Resource);

            if (includeAllResources)
            {
                resourceDictionary.Add((KnownResourceTypes.Patient, patientId), patientResponse.Resource);
                resourceDictionary.Add((KnownResourceTypes.Observation, observationId), observationResponse.Resource);
                resourceDictionary.Add((KnownResourceTypes.Group, groupId), groupResponse.Resource);
            }

            return (resourceDictionary, groupId);
        }
    }
}
