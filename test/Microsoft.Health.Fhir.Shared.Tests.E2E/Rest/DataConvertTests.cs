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
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;
using Microsoft.Azure.ContainerRegistry;
using Microsoft.Azure.ContainerRegistry.Models;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.DataConvert.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Fhir.Tests.E2E.Rest;
using Microsoft.Health.Test.Utilities;
using Microsoft.Rest;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Shared.Tests.E2E.Rest
{
    /// <summary>
    /// End to end tests using both default & customized template set.
    /// Tests using customized template set will not run locally since there is no acr emulator.
    /// </summary>
    [Trait(Traits.Category, Categories.DataConvert)]
    [HttpIntegrationFixtureArgumentSets(DataStore.CosmosDb, Format.Json)]
    public class DataConvertTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private const string DefaultTemplateSetReference = "microsofthealth/fhirconverter:default";
        private const string TemplateSetFile = "TestData/DataConvert/DefaultTemplates.tar.gz";
        private const string TestRepositoryName = "templatetest";
        private const string TestRepositoryTag = "v0.1";

        private bool _isUsingInProcTestServer = false;
        private readonly TestFhirClient _testFhirClient;
        private readonly DataConvertConfiguration _dataConvertConfiguration;

        public DataConvertTests(HttpIntegrationTestFixture fixture)
        {
            _isUsingInProcTestServer = fixture.IsUsingInProcTestServer;
            _testFhirClient = fixture.TestFhirClient;
            _dataConvertConfiguration = ((IOptions<DataConvertConfiguration>)(fixture.TestFhirServer as InProcTestFhirServer)?.Server?.Services?.GetService(typeof(IOptions<DataConvertConfiguration>)))?.Value;
        }

        [Fact]
        public async Task GivenAValidRequestWithDefaultTemplateSet_WhenDataConvert_CorrectResponseShouldReturn()
        {
            var parameters = GetDataConvertParams(GetSampleHl7v2Message(), "hl7v2", DefaultTemplateSetReference, "ADT_A01");
            var requestMessage = GenerateDataConvertRequest(parameters);
            HttpResponseMessage response = await _testFhirClient.HttpClient.SendAsync(requestMessage);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var bundleContent = await response.Content.ReadAsStringAsync();
            var parser = new FhirJsonParser();
            var bundleResource = parser.Parse<Bundle>(bundleContent);
            Assert.Equal("urn:uuid:b06a26a8-9cb6-ef2c-b4a7-3781a6f7f71a", bundleResource.Entry.First().FullUrl);
        }

        [Theory]
        [InlineData("           ")]
        [InlineData("cda")]
        [InlineData("fhir")]
        [InlineData("jpeg")]
        public async Task GivenAValidRequestWithInvalidInputDataType_WhenDataConvert_ShouldReturnBadRequest(string inputDataType)
        {
            var parameters = GetDataConvertParams(GetSampleHl7v2Message(), inputDataType, DefaultTemplateSetReference, "ADT_A01");

            var requestMessage = GenerateDataConvertRequest(parameters);
            HttpResponseMessage response = await _testFhirClient.HttpClient.SendAsync(requestMessage);
            var responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("Value of the following parameter inputDataType is invalid", responseContent);
        }

        [Theory]
        [InlineData("data")]
        [InlineData("*&^%")]
        public async Task GivenAInvalidRequestWithUnsupportedParameter_WhenDataConvert_ShouldReturnBadRequest(string unsupportedParameter)
        {
            var parameters = GetDataConvertParams(GetSampleHl7v2Message(), "hl7v2", DefaultTemplateSetReference, "ADT_A01");
            parameters.Parameter.Add(new Parameters.ParameterComponent { Name = unsupportedParameter, Value = new FhirString("test") });

            var requestMessage = GenerateDataConvertRequest(parameters);
            HttpResponseMessage response = await _testFhirClient.HttpClient.SendAsync(requestMessage);
            var responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains($"Data convert does not support the following parameter {unsupportedParameter}", responseContent);
        }

        [Theory]
        [InlineData("test.azurecr.io")]
        [InlineData("template:default")]
        [InlineData("/template:default")]
        public async Task GivenAValidRequest_ButTemplateReferenceIsInvalid_WhenDataConvert_ShouldReturnBadRequest(string templateReference)
        {
            var parameters = GetDataConvertParams(GetSampleHl7v2Message(), "hl7v2", templateReference, "ADT_A01");

            var requestMessage = GenerateDataConvertRequest(parameters);
            HttpResponseMessage response = await _testFhirClient.HttpClient.SendAsync(requestMessage);
            var responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("Template reference is invalid", responseContent);
        }

        [Theory]
        [InlineData("fakeacr.azurecr.io/template:default")]
        [InlineData("test.azurecr-test.io/template:default")]
        [InlineData("test.azurecr.com/template@sha256:592535ef52d742f81e35f4d87b43d9b535ed56cf58c90a14fc5fd7ea0fbb8696")]
        [InlineData("*****####.com/template:default")]
        [InlineData("¶Š™œãý£¾.com/template:default")]
        public async Task GivenAValidRequest_ButTemplateRegistryIsNotRegistered_WhenDataConvert_ShouldReturnBadRequest(string templateReference)
        {
            var parameters = GetDataConvertParams(GetSampleHl7v2Message(), "hl7v2", templateReference, "ADT_A01");

            var requestMessage = GenerateDataConvertRequest(parameters);
            HttpResponseMessage response = await _testFhirClient.HttpClient.SendAsync(requestMessage);
            var responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var registryName = templateReference.Split('/')[0];
            Assert.Contains($"The container registry {registryName} is not registered.", responseContent);
        }

        [Theory]
        [InlineData("123")]
        [InlineData("MSH*")]
        [InlineData("MSH|SIMHOSP|SFAC|RAPP|RFAC|20200508131015||ADT^A01|517|T|2.3|||AL||44|ASCII\nEVN|A01|20200508131015|||C005^Whittingham^Sylvia^^^Dr^^^DRNBR^PRSNL^^^ORGDR|\nPID|1|3735064194^^^SIMULATOR MRN^MRN|3735064194^^^SIMULATOR MRN^MRN~2021051528^^^NHSNBR^NHSNMBR||Kinmonth^Joanna^Chelsea^^Ms^^CURRENT||19870624000000|F|||89 Transaction House^Handmaiden Street^Wembley^^FV75 4GJ^GBR^HOME||020 3614 5541^HOME|||||||||C^White - Other^^^||||||||\nPD1|||FAMILY PRACTICE^^12345|\nPV1|1|I|OtherWard^MainRoom^Bed 183^Simulated Hospital^^BED^Main Building^4|28b|||C005^Whittingham^Sylvia^^^Dr^^^DRNBR^PRSNL^^^ORGDR|||CAR|||||||||16094728916771313876^^^^visitid||||||||||||||||||||||ARRIVED|||20200508131015||")]
        public async Task GivenAValidRequest_ButInputDataIsNotValidHl7V2Message_WhenDataConvert_ShouldReturnBadRequest(string inputData)
        {
            var parameters = GetDataConvertParams(inputData, "hl7v2", DefaultTemplateSetReference, "ADT_A01");

            var requestMessage = GenerateDataConvertRequest(parameters);
            HttpResponseMessage response = await _testFhirClient.HttpClient.SendAsync(requestMessage);
            var responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains($"Unable to parse the inputData to Hl7v2 type.", responseContent);
        }

        // The following tests needs to be run with cloud environment.

        [Fact]
        public async Task GivenAValidRequestWithCustomizedTemplateSet_WhenDataConvert_CorrectResponseShouldReturn()
        {
            if (_isUsingInProcTestServer)
            {
                return;
            }

            var registry = GetTestContainerRegistryInfo();
            await PushTemplateSet(registry, TestRepositoryName, TestRepositoryTag);

            var parameters = GetDataConvertParams(GetSampleHl7v2Message(), "hl7v2", $"{registry.ContainerRegistryServer}/{TestRepositoryName}:{TestRepositoryTag}", "ADT_A01");
            var requestMessage = GenerateDataConvertRequest(parameters);
            HttpResponseMessage response = await _testFhirClient.HttpClient.SendAsync(requestMessage);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var bundleContent = await response.Content.ReadAsStringAsync();
            var parser = new FhirJsonParser();
            var bundleResource = parser.Parse<Bundle>(bundleContent);
            Assert.Equal("urn:uuid:b06a26a8-9cb6-ef2c-b4a7-3781a6f7f71a", bundleResource.Entry.First().FullUrl);
        }

        [Theory]
        [InlineData("template:1234567890")]
        [InlineData("wrongtemplate:default")]
        [InlineData("template@sha256:592535ef52d742f81e35f4d87b43d9b535ed56cf58c90a14fc5fd7ea0fbb8695")]
        public async Task GivenAValidRequest_ButTemplateSetIsNotFound_WhenDataConvert_ShouldReturnBadRequest(string imageReference)
        {
            if (_isUsingInProcTestServer)
            {
                return;
            }

            var registry = GetTestContainerRegistryInfo();
            await PushTemplateSet(registry, TestRepositoryName, TestRepositoryTag);

            var parameters = GetDataConvertParams(GetSampleHl7v2Message(), "hl7v2", $"{registry.ContainerRegistryServer}/{imageReference}", "ADT_A01");

            var requestMessage = GenerateDataConvertRequest(parameters);
            HttpResponseMessage response = await _testFhirClient.HttpClient.SendAsync(requestMessage);
            var responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains($"Failed to get template set.", responseContent);
        }

        private HttpRequestMessage GenerateDataConvertRequest(
            Parameters inputParameters,
            string path = "$data-convert",
            string acceptHeader = ContentType.JSON_CONTENT_HEADER,
            string preferHeader = "respond-async",
            Dictionary<string, string> queryParams = null)
        {
            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Post,
            };

            request.Content = new StringContent(inputParameters.ToJson(), System.Text.Encoding.UTF8, "application/json");
            request.RequestUri = new Uri(_testFhirClient.HttpClient.BaseAddress, path);

            return request;
        }

        private static Parameters GetDataConvertParams(string inputData, string inputDataType, string templateSetReference, string entryPointTemplate)
        {
            var parametersResource = new Parameters();
            parametersResource.Parameter = new List<Parameters.ParameterComponent>();

            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = OperationParameterProperties.InputData, Value = new FhirString(inputData) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = OperationParameterProperties.InputDataType, Value = new FhirString(inputDataType) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = OperationParameterProperties.TemplateSetReference, Value = new FhirString(templateSetReference) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = OperationParameterProperties.EntryPointTemplate, Value = new FhirString(entryPointTemplate) });

            return parametersResource;
        }

        private async Task PushTemplateSet(ContainerRegistryInfo registry, string repository, string tag)
        {
            AzureContainerRegistryClient acrClient = new AzureContainerRegistryClient(registry.ContainerRegistryServer, new AcrBasicToken(registry));

            int schemaV2 = 2;
            string mediatypeV2Manifest = "application/vnd.docker.distribution.manifest.v2+json";
            string mediatypeV1Manifest = "application/vnd.oci.image.config.v1+json";
            string emptyConfigStr = "{}";

            // Upload config blob
            byte[] originalConfigBytes = Encoding.UTF8.GetBytes(emptyConfigStr);
            using var originalConfigStream = new MemoryStream(originalConfigBytes);
            string originalConfigDigest = ComputeDigest(originalConfigStream);
            await UploadBlob(acrClient, originalConfigStream, repository, originalConfigDigest);

            // Upload memory blob
            using FileStream fileStream = File.OpenRead(TemplateSetFile);
            using MemoryStream byteStream = new MemoryStream();
            fileStream.CopyTo(byteStream);
            var blobLength = byteStream.Length;
            string blobDigest = ComputeDigest(byteStream);
            await UploadBlob(acrClient, byteStream, repository, blobDigest);

            // Push manifest
            List<Descriptor> layers = new List<Descriptor>
            {
                new Descriptor("application/vnd.oci.image.layer.v1.tar", blobLength, blobDigest),
            };
            var v2Manifest = new V2Manifest(schemaV2, mediatypeV2Manifest, new Descriptor(mediatypeV1Manifest, originalConfigBytes.Length, originalConfigDigest), layers);
            await acrClient.Manifests.CreateAsync(repository, tag, v2Manifest);
        }

        private async Task UploadBlob(AzureContainerRegistryClient acrClient, Stream stream, string repository, string digest)
        {
            stream.Position = 0;
            var uploadInfo = await acrClient.Blob.StartUploadAsync(repository);
            var uploadedLayer = await acrClient.Blob.UploadAsync(stream, uploadInfo.Location);
            await acrClient.Blob.EndUploadAsync(digest, uploadedLayer.Location);
        }

        private static string ComputeDigest(Stream s)
        {
            s.Position = 0;
            StringBuilder sb = new StringBuilder();

            using (var hash = SHA256.Create())
            {
                byte[] result = hash.ComputeHash(s);
                foreach (byte b in result)
                {
                    sb.Append(b.ToString("x2"));
                }
            }

            return "sha256:" + sb.ToString();
        }

        private static string GetSampleHl7v2Message()
        {
            return "MSH|^~\\&|SIMHOSP|SFAC|RAPP|RFAC|20200508131015||ADT^A01|517|T|2.3|||AL||44|ASCII\nEVN|A01|20200508131015|||C005^Whittingham^Sylvia^^^Dr^^^DRNBR^PRSNL^^^ORGDR|\nPID|1|3735064194^^^SIMULATOR MRN^MRN|3735064194^^^SIMULATOR MRN^MRN~2021051528^^^NHSNBR^NHSNMBR||Kinmonth^Joanna^Chelsea^^Ms^^CURRENT||19870624000000|F|||89 Transaction House^Handmaiden Street^Wembley^^FV75 4GJ^GBR^HOME||020 3614 5541^HOME|||||||||C^White - Other^^^||||||||\nPD1|||FAMILY PRACTICE^^12345|\nPV1|1|I|OtherWard^MainRoom^Bed 183^Simulated Hospital^^BED^Main Building^4|28b|||C005^Whittingham^Sylvia^^^Dr^^^DRNBR^PRSNL^^^ORGDR|||CAR|||||||||16094728916771313876^^^^visitid||||||||||||||||||||||ARRIVED|||20200508131015||";
        }

        private ContainerRegistryInfo GetTestContainerRegistryInfo()
        {
            ContainerRegistryInfo containerRegistry = _dataConvertConfiguration?.ContainerRegistries?.FirstOrDefault();
            if (containerRegistry == null || string.IsNullOrWhiteSpace(containerRegistry.ContainerRegistryServer))
            {
                containerRegistry = new ContainerRegistryInfo
                {
                    ContainerRegistryServer = Environment.GetEnvironmentVariable("TestContainerRegistryServer"),
                    ContainerRegistryUsername = Environment.GetEnvironmentVariable("TestContainerRegistryServer").Split('.')[0],
                    ContainerRegistryPassword = Environment.GetEnvironmentVariable("TestContainerRegistryPassword"),
                };
            }

            if (containerRegistry == null || string.IsNullOrEmpty(containerRegistry.ContainerRegistryServer))
            {
                throw new Exception("Unable to find a valid container registry.");
            }

            return containerRegistry;
        }

        internal class AcrBasicToken : ServiceClientCredentials
        {
            private ContainerRegistryInfo _registry;

            public AcrBasicToken(ContainerRegistryInfo registry)
            {
                _registry = registry;
            }

            public override void InitializeServiceClient<T>(ServiceClient<T> client)
            {
                base.InitializeServiceClient(client);
            }

            public override Task ProcessHttpRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var basicToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_registry.ContainerRegistryUsername}:{_registry.ContainerRegistryPassword}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicToken);
                return base.ProcessHttpRequestAsync(request, cancellationToken);
            }
        }
    }
}
