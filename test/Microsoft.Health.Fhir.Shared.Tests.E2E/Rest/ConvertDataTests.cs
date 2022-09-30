// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    /// <summary>
    /// End to end tests using default template collection only (no container registry configurations needed).
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.ConvertData)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class ConvertDataTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private const string DefaultTemplateSetReference = "microsofthealth/fhirconverter:default";
        private const string Hl7v2DefaultTemplateSetReference = "microsofthealth/hl7v2templates:default";
        private const string CcdaDefaultTemplateSetReference = "microsofthealth/ccdatemplates:default";
        private const string JsonDefaultTemplateSetReference = "microsofthealth/jsontemplates:default";
        private const string FhirStu3ToR4DefaultTemplateSetReference = "microsofthealth/stu3tor4templates:default";

        private readonly TestFhirClient _testFhirClient;
        private readonly bool _convertDataEnabled = false;

        public ConvertDataTests(HttpIntegrationTestFixture fixture)
        {
            _testFhirClient = fixture.TestFhirClient;
            var convertDataConfiguration = ((IOptions<ConvertDataConfiguration>)(fixture.TestFhirServer as InProcTestFhirServer)?.Server?.Services?.GetService(typeof(IOptions<ConvertDataConfiguration>)))?.Value;
            _convertDataEnabled = convertDataConfiguration?.Enabled ?? false;
        }

        [SkippableTheory]
        [InlineData(DefaultTemplateSetReference)]
        [InlineData(Hl7v2DefaultTemplateSetReference)]
        public async Task GivenAHl7v2ValidRequestWithDefaultTemplateSet_WhenConvertData_CorrectResponseShouldReturn(string templateReference)
        {
            Skip.IfNot(_convertDataEnabled);

            var parameters = GetConvertDataParams(Samples.SampleHl7v2Message, "hl7v2", templateReference, "ADT_A01");
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

        [SkippableFact]
        public async Task GivenACcdaValidRequestWithDefaultTemplateSet_WhenConvertData_CorrectResponseShouldReturn()
        {
            Skip.IfNot(_convertDataEnabled);

            var parameters = GetConvertDataParams(Samples.SampleCcdaMessage, "Ccda", CcdaDefaultTemplateSetReference, "CCD");
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

        [SkippableFact]
        public async Task GivenAJsonValidRequestWithDefaultTemplateSet_WhenConvertData_CorrectResponseShouldReturn()
        {
            Skip.IfNot(_convertDataEnabled);

            var parameters = GetConvertDataParams(Samples.SampleJsonMessage, "Json", JsonDefaultTemplateSetReference, "ExamplePatient");
            var requestMessage = GenerateConvertDataRequest(parameters);
            HttpResponseMessage response = await _testFhirClient.HttpClient.SendAsync(requestMessage);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var patientContent = await response.Content.ReadAsStringAsync();
            var setting = new ParserSettings
            {
                AcceptUnknownMembers = true,
                PermissiveParsing = true,
            };
            var parser = new FhirJsonParser(setting);
            var patientResource = parser.Parse<Patient>(patientContent);
            Assert.NotEmpty(patientResource.Id);
        }

        [SkippableFact]
        public async Task GivenAFhirValidRequestWithDefaultTemplateSet_WhenConvertData_CorrectResponseShouldReturn()
        {
            Skip.IfNot(_convertDataEnabled);

            var parameters = GetConvertDataParams(Samples.SampleFhirStu3Message, "Fhir", FhirStu3ToR4DefaultTemplateSetReference, "Patient");
            var requestMessage = GenerateConvertDataRequest(parameters);
            HttpResponseMessage response = await _testFhirClient.HttpClient.SendAsync(requestMessage);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var patientContent = await response.Content.ReadAsStringAsync();
            var setting = new ParserSettings
            {
                AcceptUnknownMembers = true,
                PermissiveParsing = true,
            };
            var parser = new FhirJsonParser(setting);
            var patientResource = parser.Parse<Patient>(patientContent);
            Assert.NotEmpty(patientResource.Id);
        }

        [SkippableTheory]
        [InlineData("           ")]
        [InlineData("cda")]
        [InlineData("fhirstu3")]
        [InlineData("jpeg")]
        public async Task GivenAValidRequestWithInvalidInputDataType_WhenConvertData_ShouldReturnBadRequest(string inputDataType)
        {
            Skip.IfNot(_convertDataEnabled);

            var parameters = GetConvertDataParams(Samples.SampleHl7v2Message, inputDataType, DefaultTemplateSetReference, "ADT_A01");

            var requestMessage = GenerateConvertDataRequest(parameters);
            HttpResponseMessage response = await _testFhirClient.HttpClient.SendAsync(requestMessage);
            var responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("Value of the following parameter inputDataType is invalid", responseContent);
        }

        [SkippableTheory]
        [InlineData("data")]
        [InlineData("*&^%")]
        public async Task GivenAInvalidRequestWithUnsupportedParameter_WhenConvertData_ShouldReturnBadRequest(string unsupportedParameter)
        {
            Skip.IfNot(_convertDataEnabled);

            var parameters = GetConvertDataParams(Samples.SampleHl7v2Message, "hl7v2", DefaultTemplateSetReference, "ADT_A01");
            parameters.Parameter.Add(new Parameters.ParameterComponent { Name = unsupportedParameter, Value = new FhirString("test") });

            var requestMessage = GenerateConvertDataRequest(parameters);
            HttpResponseMessage response = await _testFhirClient.HttpClient.SendAsync(requestMessage);
            var responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains($"Convert data does not support the following parameter {unsupportedParameter}", responseContent);
        }

        [SkippableTheory]
        [InlineData("test.azurecr.io")]
        [InlineData("template:default")]
        [InlineData("/template:default")]
        public async Task GivenAValidRequest_ButTemplateReferenceIsInvalid_WhenConvertData_ShouldReturnBadRequest(string templateReference)
        {
            Skip.IfNot(_convertDataEnabled);

            var parameters = GetConvertDataParams(Samples.SampleHl7v2Message, "hl7v2", templateReference, "ADT_A01");

            var requestMessage = GenerateConvertDataRequest(parameters);
            HttpResponseMessage response = await _testFhirClient.HttpClient.SendAsync(requestMessage);
            var responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains($"The template collection reference '{templateReference}' is invalid", responseContent);
        }

        [SkippableTheory]
        [InlineData("fakeacr.azurecr.io/template:default")]
        [InlineData("test.azurecr-test.io/template:default")]
        [InlineData("test.azurecr.com/template@sha256:592535ef52d742f81e35f4d87b43d9b535ed56cf58c90a14fc5fd7ea0fbb8696")]
        [InlineData("*****####.com/template:default")]
        [InlineData("¶Š™œãý£¾.com/template:default")]
        public async Task GivenAValidRequest_ButTemplateReferenceIsNotConfigured_WhenConvertData_ShouldReturnBadRequest(string templateReference)
        {
            Skip.IfNot(_convertDataEnabled);

            var parameters = GetConvertDataParams(Samples.SampleHl7v2Message, "hl7v2", templateReference, "ADT_A01");

            var requestMessage = GenerateConvertDataRequest(parameters);
            HttpResponseMessage response = await _testFhirClient.HttpClient.SendAsync(requestMessage);
            var responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var registryName = templateReference.Split('/')[0];
            Assert.Contains($"The template collection reference '{templateReference}' is not configured.", responseContent);
        }

        [SkippableTheory]
        [InlineData("123")]
        [InlineData("MSH*")]
        [InlineData("MSH|SIMHOSP|SFAC|RAPP|RFAC|20200508131015||ADT^A01|517|T|2.3|||AL||44|ASCII\nEVN|A01|20200508131015|||C005^Whittingham^Sylvia^^^Dr^^^DRNBR^PRSNL^^^ORGDR|\nPID|1|3735064194^^^SIMULATOR MRN^MRN|3735064194^^^SIMULATOR MRN^MRN~2021051528^^^NHSNBR^NHSNMBR||Kinmonth^Joanna^Chelsea^^Ms^^CURRENT||19870624000000|F|||89 Transaction House^Handmaiden Street^Wembley^^FV75 4GJ^GBR^HOME||020 3614 5541^HOME|||||||||C^White - Other^^^||||||||\nPD1|||FAMILY PRACTICE^^12345|\nPV1|1|I|OtherWard^MainRoom^Bed 183^Simulated Hospital^^BED^Main Building^4|28b|||C005^Whittingham^Sylvia^^^Dr^^^DRNBR^PRSNL^^^ORGDR|||CAR|||||||||16094728916771313876^^^^visitid||||||||||||||||||||||ARRIVED|||20200508131015||")]
        public async Task GivenAnHl7v2ValidRequestWithDefaultTemplateSet_ButInputDataIsNotValidHl7V2Message_WhenConvertData_ShouldReturnBadRequest(string inputData)
        {
            Skip.IfNot(_convertDataEnabled);

            var parameters = GetConvertDataParams(inputData, "hl7v2", DefaultTemplateSetReference, "ADT_A01");

            var requestMessage = GenerateConvertDataRequest(parameters);
            HttpResponseMessage response = await _testFhirClient.HttpClient.SendAsync(requestMessage);
            var responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains($"The input data could not be parsed correctly", responseContent);
        }

        [SkippableTheory]
        [InlineData("Abc")]
        [InlineData("¶Š™œãý£¾")]
        [InlineData("<?xml version=\"1.0\"?><?xml-stylesheet type='text/xsl' href=''?>")]
        public async Task GivenACcdaValidRequestWithDefaultTemplateSet_ButInputDataIsNotValidCcdaDocument_WhenConvertData_ShouldReturnBadRequest(string inputData)
        {
            Skip.IfNot(_convertDataEnabled);

            var parameters = GetConvertDataParams(inputData, "Ccda", CcdaDefaultTemplateSetReference, "CCD");
            var requestMessage = GenerateConvertDataRequest(parameters);
            HttpResponseMessage response = await _testFhirClient.HttpClient.SendAsync(requestMessage);

            var responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains($"The input data could not be parsed correctly", responseContent);
        }

        [SkippableTheory]
        [InlineData("abc")]
        [InlineData("{\"a\": \"b\"]")]
        [InlineData("{\"a\": 99 * 0.6}")]
        [InlineData("[\"a\": \"b\"]")]
        [InlineData("{\"a\"}")]
        public async Task GivenAJsonValidRequestWithDefaultTemplateSet_ButInputDataIsNotValidJson_WhenConvertData_ShouldReturnBadRequest(string inputData)
        {
            Skip.IfNot(_convertDataEnabled);

            var parameters = GetConvertDataParams(inputData, "json", JsonDefaultTemplateSetReference, "ExamplePatient");

            var requestMessage = GenerateConvertDataRequest(parameters);
            HttpResponseMessage response = await _testFhirClient.HttpClient.SendAsync(requestMessage);
            var responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("The input data could not be parsed correctly", responseContent);
        }

        [SkippableTheory]
        [InlineData("abc")]
        [InlineData("{\"a\": \"b\"]")]
        [InlineData("{\"a\": 99 * 0.6}")]
        [InlineData("[\"a\": \"b\"]")]
        [InlineData("{\"a\"}")]
        public async Task GivenAFhirValidRequestWithDefaultTemplateSet_ButInputDataIsNotValidJson_WhenConvertData_ShouldReturnBadRequest(string inputData)
        {
            Skip.IfNot(_convertDataEnabled);

            var parameters = GetConvertDataParams(inputData, "Fhir", FhirStu3ToR4DefaultTemplateSetReference, "Patient");

            var requestMessage = GenerateConvertDataRequest(parameters);
            HttpResponseMessage response = await _testFhirClient.HttpClient.SendAsync(requestMessage);
            var responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("The input data could not be parsed correctly", responseContent);
        }

        [SkippableTheory]
        [InlineData(Samples.SampleHl7v2Message, "Ccda", Hl7v2DefaultTemplateSetReference, "ADT_A01")]
        [InlineData(Samples.SampleCcdaMessage, "Json", CcdaDefaultTemplateSetReference, "CCD")]
        [InlineData(Samples.SampleJsonMessage, "Hl7v2", JsonDefaultTemplateSetReference, "ExamplePatient")]
        [InlineData(Samples.SampleFhirStu3Message, "Ccda", FhirStu3ToR4DefaultTemplateSetReference, "Patient")]
        [InlineData(Samples.SampleHl7v2Message, "Hl7v2", CcdaDefaultTemplateSetReference, "ADT_A01")]
        [InlineData(Samples.SampleCcdaMessage, "Ccda", JsonDefaultTemplateSetReference, "CCD")]
        [InlineData(Samples.SampleJsonMessage, "Json", Hl7v2DefaultTemplateSetReference, "ExamplePatient")]
        [InlineData(Samples.SampleFhirStu3Message, "Fhir", CcdaDefaultTemplateSetReference, "Patient")]
        public async Task GivenAValidRequestWithDefaultTemplateSet_ButInconsistentInputDataTypeAndDefaultTemplateSetReference_WhenConvertData_ShouldReturnBadRequest(string inputData, string inputDataType, string templateSetReference, string rootTemplate)
        {
            Skip.IfNot(_convertDataEnabled);

            var parameters = GetConvertDataParams(inputData, inputDataType, templateSetReference, rootTemplate);
            var requestMessage = GenerateConvertDataRequest(parameters);
            HttpResponseMessage response = await _testFhirClient.HttpClient.SendAsync(requestMessage);

            var responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains($"The input data type '{inputDataType}' and default template collection '{templateSetReference}' are inconsistent.", responseContent);
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
