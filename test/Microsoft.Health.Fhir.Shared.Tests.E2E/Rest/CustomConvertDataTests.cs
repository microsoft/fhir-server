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
using Microsoft.Health.Fhir.TemplateManagement;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Microsoft.Rest;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    /// <summary>
    /// Tests using customized template set will not run without extra container registry info
    /// since there is no acr emulator.
    /// </summary>
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.CustomConvertData)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class CustomConvertDataTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private const string TestRepositoryName = "conversiontemplatestest";
        private const string TestRepositoryTag = "test1.0";

        private readonly TestFhirClient _testFhirClient;
        private readonly ConvertDataConfiguration _convertDataConfiguration;

        public CustomConvertDataTests(HttpIntegrationTestFixture fixture)
        {
            _testFhirClient = fixture.TestFhirClient;
            _convertDataConfiguration = ((IOptions<ConvertDataConfiguration>)(fixture.TestFhirServer as InProcTestFhirServer)?.Server?.Services?.GetService(typeof(IOptions<ConvertDataConfiguration>)))?.Value;
        }

        [SkippableFact]
        public async Task GivenAValidRequestWithCustomizedTemplateSet_WhenConvertData_CorrectResponseShouldReturn()
        {
            var registry = GetTestContainerRegistryInfo();

            // Here we skip local E2E test since we need Managed Identity for container registry token.
            // We also skip the case when environmental variable is not provided (not able to upload templates)
            Skip.If(_convertDataConfiguration != null || registry == null);

            await PushTemplateSet(registry, TestRepositoryName, TestRepositoryTag);

            var parameters = GetConvertDataParams(Samples.SampleHl7v2Message, "hl7v2", $"{registry.Server}/{TestRepositoryName}:{TestRepositoryTag}", "ADT_A01");
            var requestMessage = GenerateConvertDataRequest(parameters);
            HttpResponseMessage response = await _testFhirClient.HttpClient.SendAsync(requestMessage);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var bundleContent = await response.Content.ReadAsStringAsync();
            var setting = new ParserSettings()
            {
                AcceptUnknownMembers = true,
                PermissiveParsing = true,
            };
            var parser = new FhirJsonParser(setting);
            var bundleResource = parser.Parse<Bundle>(bundleContent);
            Assert.NotEmpty(bundleResource.Entry.ByResourceType<Patient>().First().Id);
        }

        [SkippableTheory]
        [InlineData("template:1234567890")]
        [InlineData("wrongtemplate:default")]
        [InlineData("template@sha256:592535ef52d742f81e35f4d87b43d9b535ed56cf58c90a14fc5fd7ea0fbb8695")]
        public async Task GivenAValidRequest_ButTemplateSetIsNotFound_WhenConvertData_ShouldReturnError(string imageReference)
        {
            var registry = GetTestContainerRegistryInfo();

            // Here we skip local E2E test since we need Managed Identity for container registry token.
            // We also skip the case when environmental variable is not provided (not able to upload templates)
            Skip.If(_convertDataConfiguration != null || registry == null);

            await PushTemplateSet(registry, TestRepositoryName, TestRepositoryTag);

            var parameters = GetConvertDataParams(Samples.SampleHl7v2Message, "hl7v2", $"{registry.Server}/{imageReference}", "ADT_A01");

            var requestMessage = GenerateConvertDataRequest(parameters);
            HttpResponseMessage response = await _testFhirClient.HttpClient.SendAsync(requestMessage);
            var responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains($"Image Not Found.", responseContent);
        }

        private ContainerRegistryInfo GetTestContainerRegistryInfo()
        {
            var containerRegistry = new ContainerRegistryInfo
            {
                Server = Environment.GetEnvironmentVariable("TestContainerRegistryServer"),
                Username = Environment.GetEnvironmentVariable("TestContainerRegistryServer")?.Split('.')[0],
                Password = Environment.GetEnvironmentVariable("TestContainerRegistryPassword"),
            };

            if (string.IsNullOrEmpty(containerRegistry.Server) || string.IsNullOrEmpty(containerRegistry.Password))
            {
                return null;
            }

            return containerRegistry;
        }

        private async Task PushTemplateSet(ContainerRegistryInfo registry, string repository, string tag)
        {
            AzureContainerRegistryClient acrClient = new AzureContainerRegistryClient(registry.Server, new AcrBasicToken(registry));

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
            var defaultTemplateResourceName = $"{typeof(OciFileManager).Namespace}.Hl7v2DefaultTemplates.tar.gz";
            using Stream byteStream = typeof(OciFileManager).Assembly.GetManifestResourceStream(defaultTemplateResourceName);
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

        private HttpRequestMessage GenerateConvertDataRequest(
            Parameters inputParameters,
            string path = "$convert-data",
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

        private static Parameters GetConvertDataParams(string inputData, string inputDataType, string templateSetReference, string rootTemplate)
        {
            var parametersResource = new Parameters();
            parametersResource.Parameter = new List<Parameters.ParameterComponent>();

            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.InputData, Value = new FhirString(inputData) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.InputDataType, Value = new FhirString(inputDataType) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.TemplateCollectionReference, Value = new FhirString(templateSetReference) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.RootTemplate, Value = new FhirString(rootTemplate) });

            return parametersResource;
        }

        internal class ContainerRegistryInfo
        {
            public string Server { get; set; }

            public string Username { get; set; }

            public string Password { get; set; }
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
                var basicToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_registry.Username}:{_registry.Password}"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicToken);
                return base.ProcessHttpRequestAsync(request, cancellationToken);
            }
        }
    }
}
