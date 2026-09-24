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
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.TemplateManagement;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    /// <summary>
    /// Tests using a customized template set uploaded to Azure Container Registry.
    /// Remote runs require federated ACR configuration via environment variables consumed by
    /// <see cref="ContainerRegistryTemplateUploader.CreateFromEnvironment"/>; missing configuration
    /// is a hard failure, not a skip.  Tests are skipped only when running against an in-process
    /// test server because no ACR emulator is available in that environment.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.CustomConvertData)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class CustomConvertDataTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private const string TestRepositoryName = "conversiontemplatestest";
        private const string TestRepositoryTag = "test1.0";

        private readonly TestFhirClient _testFhirClient;
        private readonly bool _isUsingInProcTestServer;

        public CustomConvertDataTests(HttpIntegrationTestFixture fixture)
        {
            _testFhirClient = fixture.TestFhirClient;
            _isUsingInProcTestServer = fixture.IsUsingInProcTestServer;
        }

        [SkippableFact]
        public async Task GivenAValidRequestWithCustomizedTemplateSet_WhenConvertData_CorrectResponseShouldReturn()
        {
            // Here we skip local E2E test since there is no ACR emulator for in-process tests.
            Skip.If(_isUsingInProcTestServer);

            var uploader = ContainerRegistryTemplateUploader.CreateFromEnvironment(TestRepositoryName);
            await PushTemplateSet(uploader, TestRepositoryTag);

            var imageReference = $"{uploader.RegistryServer}/{TestRepositoryName}:{TestRepositoryTag}";
            var parameters = GetConvertDataParams(Samples.SampleHl7v2Message, "hl7v2", imageReference, "ADT_A01");
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
            // Here we skip local E2E test since there is no ACR emulator for in-process tests.
            Skip.If(_isUsingInProcTestServer);

            var uploader = ContainerRegistryTemplateUploader.CreateFromEnvironment(TestRepositoryName);
            await PushTemplateSet(uploader, TestRepositoryTag);

            var parameters = GetConvertDataParams(Samples.SampleHl7v2Message, "hl7v2", $"{uploader.RegistryServer}/{imageReference}", "ADT_A01");

            var requestMessage = GenerateConvertDataRequest(parameters);
            HttpResponseMessage response = await _testFhirClient.HttpClient.SendAsync(requestMessage);
            var responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains($"Image Not Found.", responseContent);
        }

        private static async Task PushTemplateSet(ContainerRegistryTemplateUploader uploader, string tag)
        {
            var resourceName = $"{typeof(OciFileManager).Namespace}.Hl7v2DefaultTemplates.tar.gz";
            using Stream stream = typeof(OciFileManager).Assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' was not found.");
            await uploader.UploadTemplateSetAsync(stream, tag);
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
    }
}
