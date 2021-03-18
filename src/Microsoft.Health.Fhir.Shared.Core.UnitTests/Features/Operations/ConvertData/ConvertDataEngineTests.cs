// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DotLiquid;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.ConvertData;
using Microsoft.Health.Fhir.Core.Features.Operations.ConvertData.Models;
using Microsoft.Health.Fhir.Core.Messages.ConvertData;
using Microsoft.Health.Fhir.Liquid.Converter.Exceptions;
using Microsoft.Health.Fhir.TemplateManagement.Models;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Operations.ConvertData
{
    public class ConvertDataEngineTests
    {
        private ConvertDataConfiguration _config;

        public ConvertDataEngineTests()
        {
            _config = new ConvertDataConfiguration
            {
                Enabled = true,
                OperationTimeout = TimeSpan.FromSeconds(1),
            };
            _config.ContainerRegistryServers.Add("test.azurecr.io");
        }

        [Fact]
        public async Task GivenHl7V2ConvertDataRequest_WithDefaultTemplates_CorrectResultShouldReturn()
        {
            var convertDataEngine = GetDefaultEngine();
            var request = GetHl7V2RequestWithDefaultTemplates();
            var response = await convertDataEngine.Process(request, CancellationToken.None);

            var setting = new ParserSettings()
            {
                AcceptUnknownMembers = true,
                PermissiveParsing = true,
            };
            var parser = new FhirJsonParser(setting);
            var bundleResource = parser.Parse<Bundle>(response.Resource);

            var patient = bundleResource.Entry.ByResourceType<Patient>().First();
            Assert.NotEmpty(patient.Id);
            Assert.Equal("DUCK", patient.Name.First().Family);
            Assert.Equal("1924-10-10", patient.BirthDate);
        }

        [Fact]
        public async Task GivenCCDAConvertDataRequest_WithADefaultTemplates_CorrectResultShouldReturn()
        {
            var convertDataEngine = GetDefaultEngine();
            var request = GetCCDARequestWithDefaultTemplates();
            var response = await convertDataEngine.Process(request, CancellationToken.None);

            var setting = new ParserSettings()
            {
                AcceptUnknownMembers = true,
                PermissiveParsing = true,
            };
            var parser = new FhirJsonParser(setting);
            var bundleResource = parser.Parse<Bundle>(response.Resource);

            var patient = bundleResource.Entry.ByResourceType<Patient>().First();
            Assert.NotEmpty(patient.Id);
            Assert.Equal("Nelson", patient.Name.First().Family);
            Assert.Equal("1962-08-28", patient.BirthDate);
        }

        [Theory]
        [InlineData("fakeacr.azurecr.io/template:default")]
        [InlineData("test.azurecr-test.io/template:default")]
        [InlineData("test.azurecr.com/template@sha256:592535ef52d742f81e35f4d87b43d9b535ed56cf58c90a14fc5fd7ea0fbb8696")]
        [InlineData("*****####.com/template:default")]
        [InlineData("¶Š™œãý£¾.com/template:default")]
        public async Task GivenConvertDataRequest_WithUnconfiguredRegistry_ContainerRegistryNotConfiguredExceptionShouldBeThrown(string templateReference)
        {
            var convertDataEngine = GetDefaultEngine();
            var hl7v2Request = GetHl7V2RequestWithTemplateReference(templateReference);
            await Assert.ThrowsAsync<ContainerRegistryNotConfiguredException>(() => convertDataEngine.Process(hl7v2Request, CancellationToken.None));

            var ccdaRequest = GetCCDARequestWithTemplateReference(templateReference);
            await Assert.ThrowsAsync<ContainerRegistryNotConfiguredException>(() => convertDataEngine.Process(ccdaRequest, CancellationToken.None));
        }

        [Theory]
        [InlineData("       ")]
        [InlineData("Abc")]
        [InlineData("¶Š™œãý£¾")]
        [InlineData("MSH|")]
        public async Task GivenHl7V2ConvertDataRequest_WithWrongInputData_ConvertDataFailedExceptionShouldBeThrown(string inputData)
        {
            var convertDataEngine = GetDefaultEngine();
            var request = GetHl7V2RequestWithInputData(inputData);
            var exception = await Assert.ThrowsAsync<ConvertDataFailedException>(() => convertDataEngine.Process(request, CancellationToken.None));
            Assert.True(exception.InnerException is DataParseException);
        }

        [Theory]
        [InlineData("       ")]
        [InlineData("Abc")]
        [InlineData("¶Š™œãý£¾")]
        [InlineData("<?xml version=\"1.0\"?><?xml-stylesheet type='text/xsl' href=''?>")]
        public async Task GivenCCDAConvertDataRequest_WithWrongInputData_ConvertDataFailedExceptionShouldBeThrown(string inputData)
        {
            var convertDataEngine = GetDefaultEngine();
            var request = GetCCDARequestWithInputData(inputData);
            var exception = await Assert.ThrowsAsync<ConvertDataFailedException>(() => convertDataEngine.Process(request, CancellationToken.None));
            Assert.True(exception.InnerException is DataParseException);
        }

        [Theory]
        [InlineData("       ")]
        [InlineData("ADT_A02")]
        [InlineData("¶Š™œãý£¾")]
        public async Task GivenHl7V2ConvertDataRequest_WithWrongRootTemplate_ConvertDataFailedExceptionShouldBeThrown(string rootTemplateName)
        {
            var convertDataEngine = GetDefaultEngine();
            var request = GetHl7V2RequestWithRootTemplate(rootTemplateName);
            var exception = await Assert.ThrowsAsync<ConvertDataFailedException>(() => convertDataEngine.Process(request, CancellationToken.None));
            Assert.Equal($"Template '{rootTemplateName}' not found", exception.InnerException.Message);
        }

        [Theory]
        [InlineData("       ")]
        [InlineData("CCD1")]
        [InlineData("¶Š™œãý£¾")]
        public async Task GivenCCDAConvertDataRequest_WithWrongRootTemplate_ConvertDataFailedExceptionShouldBeThrown(string rootTemplateName)
        {
            var convertDataEngine = GetDefaultEngine();
            var request = GetCCDARequestWithRootTemplate(rootTemplateName);
            var exception = await Assert.ThrowsAsync<ConvertDataFailedException>(() => convertDataEngine.Process(request, CancellationToken.None));
            Assert.Equal($"Template '{rootTemplateName}' not found", exception.InnerException.Message);
        }

        [Fact]
        public async Task GivenHl7V2TemplateNotInJsonFormat_WhenConvert_ExceptionShouldBeThrown()
        {
            var wrongTemplateCollection = new List<Dictionary<string, Template>>
            {
                new Dictionary<string, Template>
                {
                    { "ADT_A01", Template.Parse(@"""a"":""b""") },
                },
            };
            var convertDataEngine = GetCustomEngineWithTemplates(wrongTemplateCollection);
            var request = GetHl7V2RequestWithDefaultTemplates();
            var exception = await Assert.ThrowsAsync<ConvertDataFailedException>(() => convertDataEngine.Process(request, CancellationToken.None));
            Assert.True(exception.InnerException is PostprocessException);
        }

        [Fact]
        public async Task GivenCCDATemplateNotInJsonFormat_WhenConvert_ExceptionShouldBeThrown()
        {
            var wrongTemplateCollection = new List<Dictionary<string, Template>>
            {
                new Dictionary<string, Template>
                {
                    { "CCD", Template.Parse(@"""a"":""b""") },
                },
            };
            var convertDataEngine = GetCustomEngineWithTemplates(wrongTemplateCollection);
            var request = GetCCDARequestWithDefaultTemplates();
            var exception = await Assert.ThrowsAsync<ConvertDataFailedException>(() => convertDataEngine.Process(request, CancellationToken.None));
            Assert.True(exception.InnerException is PostprocessException);
        }

        private static ConvertDataRequest GetHl7V2RequestWithDefaultTemplates()
        {
            return new ConvertDataRequest(GetSampleHl7v2Message(), ConversionInputDataType.Hl7v2, "microsofthealth", true, GetDefaultTemplateImageReferenceByDataType(Liquid.Converter.Models.DataType.Hl7v2), "ADT_A01");
        }

        private static ConvertDataRequest GetCCDARequestWithDefaultTemplates()
        {
            return new ConvertDataRequest(GetSampleCCDAMessage(), ConversionInputDataType.CCDA, "microsofthealth", true, GetDefaultTemplateImageReferenceByDataType(Liquid.Converter.Models.DataType.Ccda), "CCD");
        }

        private static ConvertDataRequest GetHl7V2RequestWithTemplateReference(string imageReference)
        {
            return new ConvertDataRequest(GetSampleHl7v2Message(), ConversionInputDataType.Hl7v2, imageReference.Split('/')[0], false, imageReference, "ADT_A01");
        }

        private static ConvertDataRequest GetCCDARequestWithTemplateReference(string imageReference)
        {
            return new ConvertDataRequest(GetSampleCCDAMessage(), ConversionInputDataType.CCDA, imageReference.Split('/')[0], false, imageReference, "CCD");
        }

        private static ConvertDataRequest GetHl7V2RequestWithInputData(string inputData)
        {
            return new ConvertDataRequest(inputData, ConversionInputDataType.Hl7v2, "microsofthealth", true, GetDefaultTemplateImageReferenceByDataType(Liquid.Converter.Models.DataType.Hl7v2), "ADT_A01");
        }

        private static ConvertDataRequest GetCCDARequestWithInputData(string inputData)
        {
            return new ConvertDataRequest(inputData, ConversionInputDataType.CCDA, "microsofthealth", true, GetDefaultTemplateImageReferenceByDataType(Liquid.Converter.Models.DataType.Ccda), "CCD");
        }

        private static ConvertDataRequest GetHl7V2RequestWithRootTemplate(string rootTemplate)
        {
            return new ConvertDataRequest(GetSampleHl7v2Message(), ConversionInputDataType.Hl7v2, "microsofthealth", true, GetDefaultTemplateImageReferenceByDataType(Liquid.Converter.Models.DataType.Hl7v2), rootTemplate);
        }

        private static ConvertDataRequest GetCCDARequestWithRootTemplate(string rootTemplate)
        {
            return new ConvertDataRequest(GetSampleCCDAMessage(), ConversionInputDataType.CCDA, "microsofthealth", true, GetDefaultTemplateImageReferenceByDataType(Liquid.Converter.Models.DataType.Ccda), rootTemplate);
        }

        private static string GetSampleHl7v2Message()
        {
            return "MSH|^~\\&|AccMgr|1|||20050110045504||ADT^A01|599102|P|2.3||| \nEVN|A01|20050110045502||||| \nPID|1||10006579^^^1^MR^1||DUCK^DONALD^D||19241010|M||1|111 DUCK ST^^FOWL^CA^999990000^^M|1|8885551212|8885551212|1|2||40007716^^^AccMgr^VN^1|123121234|||||||||||NO \nNK1|1|DUCK^HUEY|SO|3583 DUCK RD^^FOWL^CA^999990000|8885552222||Y|||||||||||||| \nPV1|1|I|PREOP^101^1^1^^^S|3|||37^DISNEY^WALT^^^^^^AccMgr^^^^CI|||01||||1|||37^DISNEY^WALT^^^^^^AccMgr^^^^CI|2|40007716^^^AccMgr^VN|4|||||||||||||||||||1||G|||20050110045253|||||| \nGT1|1|8291|DUCK^DONALD^D||111^DUCK ST^^FOWL^CA^999990000|8885551212||19241010|M||1|123121234||||#Cartoon Ducks Inc|111^DUCK ST^^FOWL^CA^999990000|8885551212||PT| \nDG1|1|I9|71596^OSTEOARTHROS NOS-L/LEG ^I9|OSTEOARTHROS NOS-L/LEG ||A| \nIN1|1|MEDICARE|3|MEDICARE|||||||Cartoon Ducks Inc|19891001|||4|DUCK^DONALD^D|1|19241010|111^DUCK ST^^FOWL^CA^999990000|||||||||||||||||123121234A||||||PT|M|111 DUCK ST^^FOWL^CA^999990000|||||8291 \nIN2|1||123121234|Cartoon Ducks Inc|||123121234A|||||||||||||||||||||||||||||||||||||||||||||||||||||||||8885551212 \nIN1|2|NON-PRIMARY|9|MEDICAL MUTUAL CALIF.|PO BOX 94776^^HOLLYWOOD^CA^441414776||8003621279|PUBSUMB|||Cartoon Ducks Inc||||7|DUCK^DONALD^D|1|19241010|111 DUCK ST^^FOWL^CA^999990000|||||||||||||||||056269770||||||PT|M|111^DUCK ST^^FOWL^CA^999990000|||||8291 \nIN2|2||123121234|Cartoon Ducks Inc||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||8885551212 \nIN1|3|SELF PAY|1|SELF PAY|||||||||||5||1\n";
        }

        private static string GetSampleCCDAMessage()
        {
            return "<?xml version=\"1.0\"?><?xml-stylesheet type='text/xsl' href=''?><ClinicalDocument xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns=\"urn:hl7-org:v3\" xmlns:mif=\"urn:hl7-org:v3/mif\" xmlns:voc=\"urn:hl7-org:v3/voc\" xmlns:sdtc=\"urn:hl7-org:sdtc\">    <realmCode code=\"US\"/>    <typeId root=\"2.16.840.1.113883.1.3\" extension=\"POCD_HD000040\"/>    <templateId root=\"2.16.840.1.113883.10.20.22.1.1\"/>    <templateId root=\"2.16.840.1.113883.10.20.22.1.2\"/>    <id root=\"2.16.840.1.113883.3.109\" extension=\"bf6b3a62-4293-47b4-9f14-c8829a156f4b\"/>    <code code=\"34133-9\" displayName=\"SUMMARIZATION OF EPISODE NOTE\"        codeSystem=\"2.16.840.1.113883.6.1\" codeSystemName=\"LOINC\"/>    <title>Continuity of Care Document (C-CDA)</title>    <effectiveTime value=\"20170528150200-0400\"/>    <confidentialityCode code=\"N\" displayName=\"Normal Sharing\" codeSystem=\"2.16.840.1.113883.5.25\"        codeSystemName=\"HL7 Confidentiality\"/>    <languageCode code=\"en-US\"/>    <recordTarget>        <patientRole>            <!-- Here is a public id that has an external meaning based on a root OID that is publically identifiable. -->             <!-- root=\"1.3.6.1.4.1.41179.2.4\" is the assigningAutorityName for                     Direct Trust's Patient/Consumer addresses \"DT.org PATIENT\" -->            <id root=\"1.3.6.1.4.1.41179.2.4\" extension=\"lisarnelson@direct.myphd.us\"                 assigningAutorityName=\"DT.org PATIENT\"/>            <!-- More ids may be used. -->            <!-- Here is the patient's MRN at RVHS  -->            <id root=\"2.16.840.1.113883.1.111.12345\" extension=\"12345-0828\"                assigningAuthorityName=\"River Valley Health Services local patient Medical Record Number\" />            <addr>                <streetAddressLine>1 Happy Valley Road</streetAddressLine>                <city>Westerly</city>                <state>RI</state>                <postalCode>02891</postalCode>                <country nullFlavor=\"UNK\"/>            </addr>            <telecom use=\"WP\" value=\"tel:+1-4013482345\"/>            <telecom use=\"HP\" value=\"tel:+1-4016412345\"/>            <telecom value=\"mailto:lisanelson@gmail.com\"/>            <telecom value=\"mailto:lisarnelson@direct.myphd.us\"/>            <patient>                <name use=\"L\">                    <family>Nelson</family>                    <given qualifier=\"CL\">Lisa</given>                </name>                <administrativeGenderCode code=\"F\" displayName=\"Female\"                    codeSystem=\"2.16.840.1.113883.5.1\" codeSystemName=\"HL7 AdministrativeGender\"/>                <birthTime value=\"19620828\"/>                <maritalStatusCode code=\"M\" displayName=\"Married\" codeSystem=\"2.16.840.1.113883.5.2\"                    codeSystemName=\"HL7 MaritalStatus\"/>                <raceCode code=\"2106-3\" displayName=\"White\" codeSystem=\"2.16.840.1.113883.6.238\"                    codeSystemName=\"CDC Race and Ethnicity\"/>                <ethnicGroupCode code=\"2186-5\" displayName=\"Not Hispanic or Latino\"                    codeSystem=\"2.16.840.1.113883.6.238\" codeSystemName=\"CDC Race and Ethnicity\"/>                <languageCommunication>                    <templateId root=\"2.16.840.1.113883.3.88.11.32.2\"/>                    <languageCode code=\"eng\"/>                    <preferenceInd value=\"true\"/>                </languageCommunication>            </patient>            <providerOrganization>                <!-- This is a public id where the root is registered to indicate the National Provider ID -->                <id root=\"2.16.840.1.113883.4.6\" extension=\"1417947383\"                    assigningAuthorityName=\"National Provider ID\"/>                <!-- This is a public id where the root indicates this is a Provider Direct Address. -->                <!-- root=\"1.3.6.1.4.1.41179.2.1\" is the assigningAutorityName for                     Direct Trust's Covered Entity addresses \"DT.org CE\" -->                <id root=\"1.3.6.1.4.1.41179.2.1\" extension=\"rvhs@rvhs.direct.md\" assigningAutorityName=\"DT.org CE (Covered Entity)\"/>                <!-- By including a root OID attribute for a healthcare organization, you can use this information to                 indicate a patient's MRN id at that organization.-->                <id root=\"2.16.840.1.113883.1.111.12345\"                     assigningAuthorityName=\"River Valley Health Services local patient Medical Record Number\" />                <name>River Valley Health Services</name>                <telecom use=\"WP\" value=\"tel:+1-4015394321\"/>                <telecom use=\"WP\" value=\"mailto:rvhs@rvhs.direct.md\"/>                <addr>                    <streetAddressLine>823 Main Street</streetAddressLine>                    <city>River Valley</city>                    <state>RI</state>                    <postalCode>028321</postalCode>                    <country>US</country>                </addr>            </providerOrganization>        </patientRole>    </recordTarget>    ...    <component>        <structuredBody>         ...         </structuredBody>    </component></ClinicalDocument>";
        }

        private IConvertDataEngine GetDefaultEngine()
        {
            IOptions<ConvertDataConfiguration> convertDataConfiguration = Options.Create(_config);
            IContainerRegistryTokenProvider tokenProvider = Substitute.For<IContainerRegistryTokenProvider>();
            tokenProvider.GetTokenAsync(Arg.Any<string>(), default).ReturnsForAnyArgs(x => GetToken(x[0].ToString(), _config));

            ContainerRegistryTemplateProvider templateProvider = new ContainerRegistryTemplateProvider(tokenProvider, convertDataConfiguration, new NullLogger<ContainerRegistryTemplateProvider>());

            return new ConvertDataEngine(
                templateProvider,
                convertDataConfiguration,
                new NullLogger<ConvertDataEngine>());
        }

        private IConvertDataEngine GetCustomEngineWithTemplates(List<Dictionary<string, Template>> templateCollection)
        {
            var templateProvider = Substitute.For<IConvertDataTemplateProvider>();
            templateProvider.GetTemplateCollectionAsync(default, default).ReturnsForAnyArgs(templateCollection);
            return new ConvertDataEngine(templateProvider, Options.Create(_config), new NullLogger<ConvertDataEngine>());
        }

        // For unit tests, we only use the built-in templates and here returns an empty token.
        private string GetToken(string registry, ConvertDataConfiguration config)
        {
            if (!config.ContainerRegistryServers.Any(server => string.Equals(server, registry, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ContainerRegistryNotConfiguredException("Container registry not configured.");
            }

            return string.Empty;
        }

        private static string GetDefaultTemplateImageReferenceByDataType(Liquid.Converter.Models.DataType dataType)
        {
            return DefaultTemplateInfo.DefaultTemplateMap.Values.FirstOrDefault(value => value.DataType == dataType).ImageReference;
        }
    }
}
