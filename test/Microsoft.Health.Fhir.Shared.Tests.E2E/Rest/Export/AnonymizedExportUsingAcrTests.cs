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
using System.Text;
using System.Threading.Tasks;
using Azure.Storage;
using Azure.Storage.Blobs.Specialized;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.TemplateManagement.Utilities;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Fhir.Tests.E2E.Rest.Metric;
using Microsoft.Health.Test.Utilities;
using Microsoft.Rest;
using Newtonsoft.Json;
using Xunit;
using FhirGroup = Hl7.Fhir.Model.Group;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Export
{
    [Trait(Traits.OwningTeam, OwningTeam.FhirImport)]
    [Trait(Traits.Category, Categories.AnonymizedExport)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class AnonymizedExportUsingAcrTests : IClassFixture<ExportDataTestFixture>
    {
        private const string TestRepositoryName = "testanonymizationconfigs";
        private const string TestConfigName = "testconfigname.json";
        private const string TestRepositoryTag = "e2etest";

        private const string TestExportStoreUriEnvironmentVariableName = "TestExportStoreUri";
        private const string TestExportStoreKeyEnvironmentVariableName = "TestExportStoreKey";

        private bool _isUsingInProcTestServer = false;
        private readonly TestFhirClient _testFhirClient;
        private readonly MetricHandler _metricHandler;
        private const string RedactResourceIdAnonymizationConfiguration = @"
{
    ""fhirPathRules"": [
        {""path"": ""Resource.nodesByName('id')"", ""method"": ""redact""},
        {""path"": ""nodesByType('Human').name"", ""method"": ""redact""}
    ]
}";

        public AnonymizedExportUsingAcrTests(ExportDataTestFixture fixture)
        {
            _isUsingInProcTestServer = fixture.IsUsingInProcTestServer;
            _testFhirClient = fixture.TestFhirClient;
            _metricHandler = fixture.MetricHandler;
        }

        [SkippableTheory]
        [InlineData("")]
        [InlineData("Patient/")]
        public async Task GivenAValidConfigurationWithAcrReference_WhenExportingAnonymizedData_ResourceShouldBeAnonymized(string path)
        {
            var registry = GetTestContainerRegistryInfo();

            // Here we skip local E2E test since we need Managed Identity for container registry token.
            // We also skip the case when environmental variable is not provided (not able to upload configurations)
            Skip.If(_isUsingInProcTestServer || registry == null);
            await PushConfigurationAsync(registry, TestRepositoryName, TestRepositoryTag, RedactResourceIdAnonymizationConfiguration);

            _metricHandler?.ResetCount();
            var dateTime = DateTimeOffset.UtcNow;
            var resourceToCreate = Samples.GetDefaultPatient().ToPoco<Patient>();
            resourceToCreate.Id = Guid.NewGuid().ToString();
            await _testFhirClient.UpdateAsync(resourceToCreate);

            string containerName = Guid.NewGuid().ToString("N");
            string reference = $"{registry.Server}/{TestRepositoryName}:{TestRepositoryTag}";
            Uri contentLocation = await _testFhirClient.AnonymizedExportUsingAcrAsync(TestConfigName, reference, dateTime, containerName, path);
            HttpResponseMessage response = await WaitForCompleteAsync(contentLocation);
            IList<Uri> blobUris = await CheckExportStatus(response);

            IEnumerable<string> dataFromExport = await DownloadBlobAndParse(blobUris);
            FhirJsonParser parser = new FhirJsonParser();

            foreach (string content in dataFromExport)
            {
                Resource result = parser.Parse<Resource>(content);

                Assert.Contains(result.Meta.Security, c => "REDACTED".Equals(c.Code));
            }
        }

        [SkippableFact]
        public async Task GivenAValidConfigurationWithAcrReference_WhenExportingGroupAnonymizedData_ResourceShouldBeAnonymized()
        {
            var registry = GetTestContainerRegistryInfo();

            // Here we skip local E2E test since we need Managed Identity for container registry token.
            // We also skip the case when environmental variable is not provided (not able to upload configurations)
            Skip.If(_isUsingInProcTestServer || registry == null);
            await PushConfigurationAsync(registry, TestRepositoryName, TestRepositoryTag, RedactResourceIdAnonymizationConfiguration);

            _metricHandler?.ResetCount();
            var patientToCreate = Samples.GetDefaultPatient().ToPoco<Patient>();
            var dateTime = DateTimeOffset.UtcNow;
            patientToCreate.Id = Guid.NewGuid().ToString();
            var patientReponse = await _testFhirClient.UpdateAsync(patientToCreate);
            var patientId = patientReponse.Resource.Id;

            var group = new FhirGroup()
            {
                Type = FhirGroup.GroupType.Person,
                Actual = true,
                Id = Guid.NewGuid().ToString(),
                Member = new List<FhirGroup.MemberComponent>()
                {
                    new FhirGroup.MemberComponent()
                    {
                        Entity = new ResourceReference($"{KnownResourceTypes.Patient}/{patientId}"),
                    },
                },
            };
            var groupReponse = await _testFhirClient.UpdateAsync(group);
            var groupId = groupReponse.Resource.Id;

            string containerName = Guid.NewGuid().ToString("N");
            string reference = $"{registry.Server}/{TestRepositoryName}:{TestRepositoryTag}";
            Uri contentLocation = await _testFhirClient.AnonymizedExportUsingAcrAsync(TestConfigName, reference, dateTime, containerName, $"Group/{groupId}/");
            HttpResponseMessage response = await WaitForCompleteAsync(contentLocation);
            IList<Uri> blobUris = await CheckExportStatus(response);

            IEnumerable<string> dataFromExport = await DownloadBlobAndParse(blobUris);
            FhirJsonParser parser = new FhirJsonParser();

            foreach (string content in dataFromExport)
            {
                Resource result = parser.Parse<Resource>(content);

                Assert.Contains(result.Meta.Security, c => "REDACTED".Equals(c.Code));
            }

            Assert.Equal(2, dataFromExport.Count());
        }

        [SkippableTheory]
        [InlineData("configimage:1234567890")]
        [InlineData("configimage@sha256:592535ef52d742f81e35f4d87b43d9b535ed56cf58c90a14fc5fd7ea0fbb8695")]
        [InlineData("wrongimage:default")]
        public async Task GivenAInvalidAcrReference_WhenExportingAnonymizedData_ThenBadRequestShouldBeReturned(string imageReference)
        {
            var registry = GetTestContainerRegistryInfo();

            // Here we skip local E2E test since we need Managed Identity for container registry token.
            // We also skip the case when environmental variable is not provided (not able to upload configurations)
            Skip.If(_isUsingInProcTestServer || registry == null);
            await PushConfigurationAsync(registry, TestRepositoryName, TestRepositoryTag, RedactResourceIdAnonymizationConfiguration);

            _metricHandler?.ResetCount();
            var dateTime = DateTimeOffset.UtcNow;
            var resourceToCreate = Samples.GetDefaultPatient().ToPoco<Patient>();
            resourceToCreate.Id = Guid.NewGuid().ToString();
            await _testFhirClient.UpdateAsync(resourceToCreate);

            string containerName = Guid.NewGuid().ToString("N");
            string reference = $"{registry.Server}/{imageReference}";
            Uri contentLocation = await _testFhirClient.AnonymizedExportUsingAcrAsync(TestConfigName, reference, dateTime, containerName);
            HttpResponseMessage response = await WaitForCompleteAsync(contentLocation);

            var responseContent = await response.Content.ReadAsStringAsync();
            Assert.Contains($"Image Not Found.", responseContent);
        }

        [SkippableFact]
        public async Task GivenInvalidConfigurationNotInAcr_WhenExportingAnonymizedData_ThenBadRequestShouldBeReturned()
        {
            var registry = GetTestContainerRegistryInfo();

            // Here we skip local E2E test since we need Managed Identity for container registry token.
            // We also skip the case when environmental variable is not provided (not able to upload configurations)
            Skip.If(_isUsingInProcTestServer || registry == null);
            await PushConfigurationAsync(registry, TestRepositoryName, TestRepositoryTag, "Invalid Json.");

            _metricHandler?.ResetCount();
            var dateTime = DateTimeOffset.UtcNow;
            string containerName = Guid.NewGuid().ToString("N");
            string reference = $"{registry.Server}/{TestRepositoryName}:{TestRepositoryTag}";
            Uri contentLocation = await _testFhirClient.AnonymizedExportUsingAcrAsync(TestConfigName, reference, dateTime, containerName);
            HttpResponseMessage response = await WaitForCompleteAsync(contentLocation);
            string responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("Failed to parse configuration file", responseContent);
        }

        [SkippableFact]
        public async Task GivenAConfigurationNotExisted_WhenExportingAnonymizedData_ThenBadRequestShouldBeReturned()
        {
            var registry = GetTestContainerRegistryInfo();

            // Here we skip local E2E test since we need Managed Identity for container registry token.
            // We also skip the case when environmental variable is not provided (not able to upload configurations)
            Skip.If(_isUsingInProcTestServer || registry == null);
            await PushConfigurationAsync(registry, TestRepositoryName, TestRepositoryTag, RedactResourceIdAnonymizationConfiguration);

            _metricHandler?.ResetCount();
            var dateTime = DateTimeOffset.UtcNow;

            string containerName = Guid.NewGuid().ToString("N");
            string reference = $"{registry.Server}/{TestRepositoryName}:{TestRepositoryTag}";
            Uri contentLocation = await _testFhirClient.AnonymizedExportUsingAcrAsync("not-exist.json", reference, dateTime, containerName);
            HttpResponseMessage response = await WaitForCompleteAsync(contentLocation);
            string responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("Anonymization configuration 'not-exist.json' not found.", responseContent);
        }

        [SkippableFact]
        public async Task GivenALargeConfiguration_WhenExportingAnonymizedData_ThenBadRequestShouldBeReturned()
        {
            var registry = GetTestContainerRegistryInfo();

            // Here we skip local E2E test since we need Managed Identity for container registry token.
            // We also skip the case when environmental variable is not provided (not able to upload configurations)
            Skip.If(_isUsingInProcTestServer || registry == null);
            string largeConfig = new string('*', (1024 * 1024) + 1); // Large config > 1MB
            await PushConfigurationAsync(registry, TestRepositoryName, TestRepositoryTag, largeConfig);

            _metricHandler?.ResetCount();
            var dateTime = DateTimeOffset.UtcNow;

            string containerName = Guid.NewGuid().ToString("N");
            string reference = $"{registry.Server}/{TestRepositoryName}:{TestRepositoryTag}";
            Uri contentLocation = await _testFhirClient.AnonymizedExportUsingAcrAsync(TestConfigName, reference, dateTime, containerName);
            HttpResponseMessage response = await WaitForCompleteAsync(contentLocation);
            string responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("Anonymization configuration is too large", responseContent);
        }

        private async Task<HttpResponseMessage> WaitForCompleteAsync(Uri contentLocation)
        {
            HttpStatusCode resultCode = HttpStatusCode.Accepted;
            HttpResponseMessage response = null;
            while (resultCode == HttpStatusCode.Accepted)
            {
                await Task.Delay(5000);

                response = await _testFhirClient.CheckExportAsync(contentLocation);

                resultCode = response.StatusCode;
            }

            return response;
        }

        private async Task<IList<Uri>> CheckExportStatus(HttpResponseMessage response)
        {
            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new Exception($"Export request failed with status code {response.StatusCode}");
            }

            // we have got the result. Deserialize into output response.
            var contentString = await response.Content.ReadAsStringAsync();

            ExportJobResult exportJobResult = JsonConvert.DeserializeObject<ExportJobResult>(contentString);
            return exportJobResult.Output.Select(x => x.FileUri).ToList();
        }

        private async Task<IEnumerable<string>> DownloadBlobAndParse(IList<Uri> blobUri)
        {
            var result = new List<string>();

            foreach (Uri uri in blobUri)
            {
                var blob = AzureStorageBlobHelper.GetBlobClient(uri);
                var response = await blob.DownloadContentAsync();
                var allData = response.Value.Content.ToString();
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

        private static ContainerRegistryInfo GetTestContainerRegistryInfo()
        {
            string server = EnvironmentVariables.GetEnvironmentVariable(KnownEnvironmentVariableNames.TestContainerRegistryServer);

            if (string.IsNullOrEmpty(server))
            {
                return null;
            }

            return new ContainerRegistryInfo { Server = server };
        }

        private static async Task PushConfigurationAsync(ContainerRegistryInfo registry, string repository, string tag, string configContent)
        {
            var uploader = ContainerRegistryTemplateUploader.CreateFromEnvironment(repository);

            byte[] configContentBytes = Encoding.UTF8.GetBytes(configContent);
            byte[] tarGzBytes = StreamUtility.CompressToTarGz(new Dictionary<string, byte[]>() { { TestConfigName, configContentBytes } }, false);
            using Stream layerStream = new MemoryStream(tarGzBytes);

            await uploader.UploadTemplateSetAsync(layerStream, tag);
        }

        internal sealed class ContainerRegistryInfo
        {
            public string Server { get; set; }
        }
    }
}
