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
using Microsoft.Health.Fhir.Tests.E2E.Rest;
using Microsoft.Health.Test.Utilities;
using Microsoft.Rest;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Shared.Tests.E2E.Rest
{
    /// <summary>
    /// End to end tests using default template collection only (no container registry configurations needed).
    /// </summary>
    [Trait(Traits.Category, Categories.ConvertData)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class ConvertDataTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private const string DefaultTemplateSetReference = "microsofthealth/fhirconverter:default";
        private const string Hl7v2DefaultTemplateSetReference = "microsofthealth/hl7v2templates:default";
        private const string CCDADefaultTemplateSetReference = "microsofthealth/ccdatemplates:default";

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

            var parameters = GetConvertDataParams(GetSampleHl7v2Message(), "hl7v2", templateReference, "ADT_A01");
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
        public async Task GivenACCDAValidRequestWithDefaultTemplateSet_WhenConvertData_CorrectResponseShouldReturn()
        {
            Skip.IfNot(_convertDataEnabled);

            var parameters = GetConvertDataParams(GetSampleCCDAMessage(), "CCDA", CCDADefaultTemplateSetReference, "CCD");
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
        [InlineData("           ")]
        [InlineData("cda")]
        [InlineData("fhir")]
        [InlineData("jpeg")]
        public async Task GivenAValidRequestWithInvalidInputDataType_WhenConvertData_ShouldReturnBadRequest(string inputDataType)
        {
            Skip.IfNot(_convertDataEnabled);

            var parameters = GetConvertDataParams(GetSampleHl7v2Message(), inputDataType, DefaultTemplateSetReference, "ADT_A01");

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

            var parameters = GetConvertDataParams(GetSampleHl7v2Message(), "hl7v2", DefaultTemplateSetReference, "ADT_A01");
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

            var parameters = GetConvertDataParams(GetSampleHl7v2Message(), "hl7v2", templateReference, "ADT_A01");

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
        public async Task GivenAValidRequest_ButTemplateRegistryIsNotConfigured_WhenConvertData_ShouldReturnBadRequest(string templateReference)
        {
            Skip.IfNot(_convertDataEnabled);

            var parameters = GetConvertDataParams(GetSampleHl7v2Message(), "hl7v2", templateReference, "ADT_A01");

            var requestMessage = GenerateConvertDataRequest(parameters);
            HttpResponseMessage response = await _testFhirClient.HttpClient.SendAsync(requestMessage);
            var responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

            var registryName = templateReference.Split('/')[0];
            Assert.Contains($"The container registry '{registryName}' is not configured.", responseContent);
        }

        [SkippableTheory]
        [InlineData("123")]
        [InlineData("MSH*")]
        [InlineData("MSH|SIMHOSP|SFAC|RAPP|RFAC|20200508131015||ADT^A01|517|T|2.3|||AL||44|ASCII\nEVN|A01|20200508131015|||C005^Whittingham^Sylvia^^^Dr^^^DRNBR^PRSNL^^^ORGDR|\nPID|1|3735064194^^^SIMULATOR MRN^MRN|3735064194^^^SIMULATOR MRN^MRN~2021051528^^^NHSNBR^NHSNMBR||Kinmonth^Joanna^Chelsea^^Ms^^CURRENT||19870624000000|F|||89 Transaction House^Handmaiden Street^Wembley^^FV75 4GJ^GBR^HOME||020 3614 5541^HOME|||||||||C^White - Other^^^||||||||\nPD1|||FAMILY PRACTICE^^12345|\nPV1|1|I|OtherWard^MainRoom^Bed 183^Simulated Hospital^^BED^Main Building^4|28b|||C005^Whittingham^Sylvia^^^Dr^^^DRNBR^PRSNL^^^ORGDR|||CAR|||||||||16094728916771313876^^^^visitid||||||||||||||||||||||ARRIVED|||20200508131015||")]
        public async Task GivenAValidRequest_ButInputDataIsNotValidHl7V2Message_WhenConvertData_ShouldReturnBadRequest(string inputData)
        {
            Skip.IfNot(_convertDataEnabled);

            var parameters = GetConvertDataParams(inputData, "hl7v2", DefaultTemplateSetReference, "ADT_A01");

            var requestMessage = GenerateConvertDataRequest(parameters);
            HttpResponseMessage response = await _testFhirClient.HttpClient.SendAsync(requestMessage);
            var responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains($"The input data could not be parsed correctly", responseContent);
        }

        [SkippableFact]
        public async Task GivenACCDAValidRequestWithDefaultTemplateSet_ButHl7v2InputData_WhenConvertData_ShouldReturnBadRequest()
        {
            Skip.IfNot(_convertDataEnabled);

            var parameters = GetConvertDataParams(GetSampleHl7v2Message(), "CCDA", CCDADefaultTemplateSetReference, "CCD");
            var requestMessage = GenerateConvertDataRequest(parameters);
            HttpResponseMessage response = await _testFhirClient.HttpClient.SendAsync(requestMessage);

            var responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains($"The input data could not be parsed correctly", responseContent);
        }

        [SkippableFact]
        public async Task GivenACCDAValidRequestWithDefaultTemplateSet_ButHl7v2InputDataType_WhenConvertData_ShouldReturnBadRequest()
        {
            Skip.IfNot(_convertDataEnabled);

            var parameters = GetConvertDataParams(GetSampleCCDAMessage(), "Hl7v2", CCDADefaultTemplateSetReference, "CCD");
            var requestMessage = GenerateConvertDataRequest(parameters);
            HttpResponseMessage response = await _testFhirClient.HttpClient.SendAsync(requestMessage);

            var responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains($"The input datatype 'Hl7v2' and default template collection 'microsofthealth/ccdatemplates:default' are inconsistent.", responseContent);
        }

        [SkippableFact]
        public async Task GivenACCDAValidRequestWithDefaultTemplateSet_ButHl7v2Templates_WhenConvertData_ShouldReturnBadRequest()
        {
            Skip.IfNot(_convertDataEnabled);

            var parameters = GetConvertDataParams(GetSampleCCDAMessage(), "CCDA", DefaultTemplateSetReference, "CCD");
            var requestMessage = GenerateConvertDataRequest(parameters);
            HttpResponseMessage response = await _testFhirClient.HttpClient.SendAsync(requestMessage);

            var responseContent = await response.Content.ReadAsStringAsync();

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains($"The input datatype 'CCDA' and default template collection 'microsofthealth/fhirconverter:default' are inconsistent.", responseContent);
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

        private static string GetSampleHl7v2Message()
        {
            return "MSH|^~\\&|AccMgr|1|||20050110045504||ADT^A01|599102|P|2.3||| \nEVN|A01|20050110045502||||| \nPID|1||10006579^^^1^MR^1||DUCK^DONALD^D||19241010|M||1|111 DUCK ST^^FOWL^CA^999990000^^M|1|8885551212|8885551212|1|2||40007716^^^AccMgr^VN^1|123121234|||||||||||NO \nNK1|1|DUCK^HUEY|SO|3583 DUCK RD^^FOWL^CA^999990000|8885552222||Y|||||||||||||| \nPV1|1|I|PREOP^101^1^1^^^S|3|||37^DISNEY^WALT^^^^^^AccMgr^^^^CI|||01||||1|||37^DISNEY^WALT^^^^^^AccMgr^^^^CI|2|40007716^^^AccMgr^VN|4|||||||||||||||||||1||G|||20050110045253|||||| \nGT1|1|8291|DUCK^DONALD^D||111^DUCK ST^^FOWL^CA^999990000|8885551212||19241010|M||1|123121234||||#Cartoon Ducks Inc|111^DUCK ST^^FOWL^CA^999990000|8885551212||PT| \nDG1|1|I9|71596^OSTEOARTHROS NOS-L/LEG ^I9|OSTEOARTHROS NOS-L/LEG ||A| \nIN1|1|MEDICARE|3|MEDICARE|||||||Cartoon Ducks Inc|19891001|||4|DUCK^DONALD^D|1|19241010|111^DUCK ST^^FOWL^CA^999990000|||||||||||||||||123121234A||||||PT|M|111 DUCK ST^^FOWL^CA^999990000|||||8291 \nIN2|1||123121234|Cartoon Ducks Inc|||123121234A|||||||||||||||||||||||||||||||||||||||||||||||||||||||||8885551212 \nIN1|2|NON-PRIMARY|9|MEDICAL MUTUAL CALIF.|PO BOX 94776^^HOLLYWOOD^CA^441414776||8003621279|PUBSUMB|||Cartoon Ducks Inc||||7|DUCK^DONALD^D|1|19241010|111 DUCK ST^^FOWL^CA^999990000|||||||||||||||||056269770||||||PT|M|111^DUCK ST^^FOWL^CA^999990000|||||8291 \nIN2|2||123121234|Cartoon Ducks Inc||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||8885551212 \nIN1|3|SELF PAY|1|SELF PAY|||||||||||5||1\n";
        }

        private static string GetSampleCCDAMessage()
        {
            return "<?xml version=\"1.0\"?><?xml-stylesheet type='text/xsl' href=''?><ClinicalDocument xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns=\"urn:hl7-org:v3\" xmlns:mif=\"urn:hl7-org:v3/mif\" xmlns:voc=\"urn:hl7-org:v3/voc\" xmlns:sdtc=\"urn:hl7-org:sdtc\">    <realmCode code=\"US\"/>    <typeId root=\"2.16.840.1.113883.1.3\" extension=\"POCD_HD000040\"/>    <templateId root=\"2.16.840.1.113883.10.20.22.1.1\"/>    <templateId root=\"2.16.840.1.113883.10.20.22.1.2\"/>    <id root=\"2.16.840.1.113883.3.109\" extension=\"bf6b3a62-4293-47b4-9f14-c8829a156f4b\"/>    <code code=\"34133-9\" displayName=\"SUMMARIZATION OF EPISODE NOTE\"        codeSystem=\"2.16.840.1.113883.6.1\" codeSystemName=\"LOINC\"/>    <title>Continuity of Care Document (C-CDA)</title>    <effectiveTime value=\"20170528150200-0400\"/>    <confidentialityCode code=\"N\" displayName=\"Normal Sharing\" codeSystem=\"2.16.840.1.113883.5.25\"        codeSystemName=\"HL7 Confidentiality\"/>    <languageCode code=\"en-US\"/>    <recordTarget>        <patientRole>            <!-- Here is a public id that has an external meaning based on a root OID that is publically identifiable. -->             <!-- root=\"1.3.6.1.4.1.41179.2.4\" is the assigningAutorityName for                     Direct Trust's Patient/Consumer addresses \"DT.org PATIENT\" -->            <id root=\"1.3.6.1.4.1.41179.2.4\" extension=\"lisarnelson@direct.myphd.us\"                 assigningAutorityName=\"DT.org PATIENT\"/>            <!-- More ids may be used. -->            <!-- Here is the patient's MRN at RVHS  -->            <id root=\"2.16.840.1.113883.1.111.12345\" extension=\"12345-0828\"                assigningAuthorityName=\"River Valley Health Services local patient Medical Record Number\" />            <addr>                <streetAddressLine>1 Happy Valley Road</streetAddressLine>                <city>Westerly</city>                <state>RI</state>                <postalCode>02891</postalCode>                <country nullFlavor=\"UNK\"/>            </addr>            <telecom use=\"WP\" value=\"tel:+1-4013482345\"/>            <telecom use=\"HP\" value=\"tel:+1-4016412345\"/>            <telecom value=\"mailto:lisanelson@gmail.com\"/>            <telecom value=\"mailto:lisarnelson@direct.myphd.us\"/>            <patient>                <name use=\"L\">                    <family>Nelson</family>                    <given qualifier=\"CL\">Lisa</given>                </name>                <administrativeGenderCode code=\"F\" displayName=\"Female\"                    codeSystem=\"2.16.840.1.113883.5.1\" codeSystemName=\"HL7 AdministrativeGender\"/>                <birthTime value=\"19620828\"/>                <maritalStatusCode code=\"M\" displayName=\"Married\" codeSystem=\"2.16.840.1.113883.5.2\"                    codeSystemName=\"HL7 MaritalStatus\"/>                <raceCode code=\"2106-3\" displayName=\"White\" codeSystem=\"2.16.840.1.113883.6.238\"                    codeSystemName=\"CDC Race and Ethnicity\"/>                <ethnicGroupCode code=\"2186-5\" displayName=\"Not Hispanic or Latino\"                    codeSystem=\"2.16.840.1.113883.6.238\" codeSystemName=\"CDC Race and Ethnicity\"/>                <languageCommunication>                    <templateId root=\"2.16.840.1.113883.3.88.11.32.2\"/>                    <languageCode code=\"eng\"/>                    <preferenceInd value=\"true\"/>                </languageCommunication>            </patient>            <providerOrganization>                <!-- This is a public id where the root is registered to indicate the National Provider ID -->                <id root=\"2.16.840.1.113883.4.6\" extension=\"1417947383\"                    assigningAuthorityName=\"National Provider ID\"/>                <!-- This is a public id where the root indicates this is a Provider Direct Address. -->                <!-- root=\"1.3.6.1.4.1.41179.2.1\" is the assigningAutorityName for                     Direct Trust's Covered Entity addresses \"DT.org CE\" -->                <id root=\"1.3.6.1.4.1.41179.2.1\" extension=\"rvhs@rvhs.direct.md\" assigningAutorityName=\"DT.org CE (Covered Entity)\"/>                <!-- By including a root OID attribute for a healthcare organization, you can use this information to                 indicate a patient's MRN id at that organization.-->                <id root=\"2.16.840.1.113883.1.111.12345\"                     assigningAuthorityName=\"River Valley Health Services local patient Medical Record Number\" />                <name>River Valley Health Services</name>                <telecom use=\"WP\" value=\"tel:+1-4015394321\"/>                <telecom use=\"WP\" value=\"mailto:rvhs@rvhs.direct.md\"/>                <addr>                    <streetAddressLine>823 Main Street</streetAddressLine>                    <city>River Valley</city>                    <state>RI</state>                    <postalCode>028321</postalCode>                    <country>US</country>                </addr>            </providerOrganization>        </patientRole>    </recordTarget>    ...    <component>        <structuredBody>         ...         </structuredBody>    </component></ClinicalDocument>";
        }
    }
}
