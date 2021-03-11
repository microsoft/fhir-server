// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.ConvertData.Models;
using Microsoft.Health.Fhir.Core.Messages.ConvertData;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Shared.Api.UnitTests.Controllers
{
    public class ConvertDataControllerTests
    {
        private ConvertDataController _convertDataeEnabledController;
        private IMediator _mediator = Substitute.For<IMediator>();
        private HttpContext _httpContext = new DefaultHttpContext();
        private static ConvertDataConfiguration _convertDataJobConfig = new ConvertDataConfiguration() { Enabled = true };

        public ConvertDataControllerTests()
        {
            _convertDataJobConfig.ContainerRegistryServers.Add("test.azurecr.io");
            _convertDataeEnabledController = GetController(_convertDataJobConfig);
            var controllerContext = new ControllerContext() { HttpContext = _httpContext };
            _convertDataeEnabledController.ControllerContext = controllerContext;
        }

        public static TheoryData<Parameters> InvalidBody =>
            new TheoryData<Parameters>
            {
                GetParamsResourceWithTooManyParams(),
                GetParamsResourceWithMissingParams(),
                GetParamsResourceWithWrongNameParam(),
                GetParamsResourceWithUnsupportedDataType(),
                null,
            };

        public static TheoryData<Parameters> InconsistentBody =>
            new TheoryData<Parameters>
            {
                GetParamsResourceWithInconsistentParamsWrongDataType(),
                GetParamsResourceWithInconsistentParamsWrongDefaultTemplates(),
                GetParamsResourceWithInconsistentParamsWrongHl7v2DefaultTemplates(),
                GetParamsResourceWithInconsistentParamsWrongCCDADefaultTemplates(),
            };

        public static TheoryData<Parameters> Hl7v2ValidBody =>
            new TheoryData<Parameters>
            {
                GetHl7v2ValidConvertDataParams(),
                GetHl7v2ValidConvertDataParamsIgnoreCases(),
            };

        public static TheoryData<Parameters> CCDAValidBody =>
        new TheoryData<Parameters>
        {
                    GetCCDAValidConvertDataParams(),
                    GetCCDAValidConvertDataParamsIgnoreCases(),
        };

        [Theory]
        [MemberData(nameof(InvalidBody), MemberType = typeof(ConvertDataControllerTests))]
        public async Task GivenAConvertDataRequest_WhenInvalidBodySent_ThenRequestNotValidThrown(Parameters body)
        {
            _convertDataeEnabledController.ControllerContext.HttpContext.Request.Method = HttpMethods.Post;
            await Assert.ThrowsAsync<RequestNotValidException>(() => _convertDataeEnabledController.ConvertData(body));
        }

        [Theory]
        [MemberData(nameof(InconsistentBody), MemberType = typeof(ConvertDataControllerTests))]
        public async Task GivenAConvertDataRequest_WhenInconsistentBodySent_ThenInconsistentThrown(Parameters body)
        {
            _convertDataeEnabledController.ControllerContext.HttpContext.Request.Method = HttpMethods.Post;
            await Assert.ThrowsAsync<InputDataTypeAndDefaultTemplateCollectionInconsistentException>(() => _convertDataeEnabledController.ConvertData(body));
        }

        [Theory]
        [InlineData("abc.azurecr.io")]
        [InlineData("abc.azurecr.io/:tag")]
        [InlineData("testimage:tag")]
        public async Task GivenAConvertDataRequest_WithInvalidReference_WhenInvalidBodySent_ThenRequestNotValidThrown(string templateCollectionReference)
        {
            var body = GetConvertDataParamsWithCustomTemplateReference(templateCollectionReference);

            _convertDataeEnabledController.ControllerContext.HttpContext.Request.Method = HttpMethods.Post;
            await Assert.ThrowsAsync<RequestNotValidException>(() => _convertDataeEnabledController.ConvertData(body));
        }

        [Theory]
        [MemberData(nameof(Hl7v2ValidBody), MemberType = typeof(ConvertDataControllerTests))]
        public async Task GivenAHl7v2ConvertDataRequest_WithValidBody_ThenConvertDataCalledWithCorrectParams(Parameters body)
        {
            _convertDataeEnabledController.ControllerContext.HttpContext.Request.Method = HttpMethods.Post;
            _mediator.Send(Arg.Any<ConvertDataRequest>()).Returns(Task.FromResult(GetHl7v2ConvertDataResponse()));
            await _convertDataeEnabledController.ConvertData(body);
            await _mediator.Received().Send(
                Arg.Is<ConvertDataRequest>(
                     r => r.InputData.ToString().Equals(body.Parameter.Find(p => p.Name.Equals(ConvertDataProperties.InputData)).Value.ToString())
                && string.Equals(r.InputDataType.ToString(), body.Parameter.Find(p => p.Name.Equals(ConvertDataProperties.InputDataType)).Value.ToString(), StringComparison.OrdinalIgnoreCase)
                && r.TemplateCollectionReference == body.Parameter.Find(p => p.Name.Equals(ConvertDataProperties.TemplateCollectionReference)).Value.ToString()
                && r.RootTemplate == body.Parameter.Find(p => p.Name.Equals(ConvertDataProperties.RootTemplate)).Value.ToString()),
                Arg.Any<CancellationToken>());
            _mediator.ClearReceivedCalls();
        }

        [Theory]
        [MemberData(nameof(CCDAValidBody), MemberType = typeof(ConvertDataControllerTests))]
        public async Task GivenACCDAConvertDataRequest_WithValidBody_ThenConvertDataCalledWithCorrectParams(Parameters body)
        {
            _convertDataeEnabledController.ControllerContext.HttpContext.Request.Method = HttpMethods.Post;
            _mediator.Send(Arg.Any<ConvertDataRequest>()).Returns(Task.FromResult(GetCCDAConvertDataResponse()));
            await _convertDataeEnabledController.ConvertData(body);
            await _mediator.Received().Send(
                Arg.Is<ConvertDataRequest>(
                     r => r.InputData.ToString().Equals(body.Parameter.Find(p => p.Name.Equals(ConvertDataProperties.InputData)).Value.ToString())
                && string.Equals(r.InputDataType.ToString(), body.Parameter.Find(p => p.Name.Equals(ConvertDataProperties.InputDataType)).Value.ToString(), StringComparison.OrdinalIgnoreCase)
                && r.TemplateCollectionReference == body.Parameter.Find(p => p.Name.Equals(ConvertDataProperties.TemplateCollectionReference)).Value.ToString()
                && r.RootTemplate == body.Parameter.Find(p => p.Name.Equals(ConvertDataProperties.RootTemplate)).Value.ToString()),
                Arg.Any<CancellationToken>());
            _mediator.ClearReceivedCalls();
        }

        private static ConvertDataResponse GetHl7v2ConvertDataResponse()
        {
            return new ConvertDataResponse(GetSampleHl7v2ConvertDataResponse());
        }

        private static ConvertDataResponse GetCCDAConvertDataResponse()
        {
            return new ConvertDataResponse(GetSampleCCDAConvertDataResponse());
        }

        private ConvertDataController GetController(ConvertDataConfiguration convertDataConfiguration)
        {
            var operationConfig = new OperationsConfiguration()
            {
                ConvertData = convertDataConfiguration,
            };

            IOptions<OperationsConfiguration> optionsOperationConfiguration = Substitute.For<IOptions<OperationsConfiguration>>();
            optionsOperationConfiguration.Value.Returns(operationConfig);

            return new ConvertDataController(
                _mediator,
                optionsOperationConfiguration,
                NullLogger<ConvertDataController>.Instance);
        }

        private static Parameters GetParamsResourceWithWrongNameParam()
        {
            var parametersResource = new Parameters();
            parametersResource.Parameter = new List<Parameters.ParameterComponent>();

            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = "foo", Value = new FhirDecimal(5) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.InputData, Value = new FhirString(GetSampleHl7v2Message()) });

            return parametersResource;
        }

        private static Parameters GetParamsResourceWithMissingParams()
        {
            var parametersResource = new Parameters();
            parametersResource.Parameter = new List<Parameters.ParameterComponent>();

            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.InputData, Value = new FhirString(GetSampleHl7v2Message()) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.InputDataType, Value = new FhirString("Hl7v2") });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.TemplateCollectionReference, Value = new FhirString("test.azurecr.io/testimage:latest") });

            return parametersResource;
        }

        private static Parameters GetParamsResourceWithTooManyParams()
        {
            var parametersResource = new Parameters();
            parametersResource.Parameter = new List<Parameters.ParameterComponent>();

            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.InputData, Value = new FhirString(GetSampleHl7v2Message()) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.InputDataType, Value = new FhirString("Hl7v2") });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.TemplateCollectionReference, Value = new FhirString("test.azurecr.io/testimage:latest") });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.RootTemplate, Value = new FhirString("ADT_A01") });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = "foo", Value = new FhirDecimal(5) });

            return parametersResource;
        }

        private static Parameters GetParamsResourceWithInconsistentParamsWrongDataType()
        {
            var parametersResource = new Parameters();
            parametersResource.Parameter = new List<Parameters.ParameterComponent>();

            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.InputData, Value = new FhirString(GetSampleHl7v2Message()) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.InputDataType, Value = new FhirString("CCDA") });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.TemplateCollectionReference, Value = new FhirString("microsofthealth/fhirconverter:default") });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.RootTemplate, Value = new FhirString("ADT_A01") });

            return parametersResource;
        }

        private static Parameters GetParamsResourceWithInconsistentParamsWrongDefaultTemplates()
        {
            var parametersResource = new Parameters();
            parametersResource.Parameter = new List<Parameters.ParameterComponent>();

            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.InputData, Value = new FhirString(GetSampleCCDAMessage()) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.InputDataType, Value = new FhirString("CCDA") });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.TemplateCollectionReference, Value = new FhirString("microsofthealth/fhirconverter:default") });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.RootTemplate, Value = new FhirString("CCD") });

            return parametersResource;
        }

        private static Parameters GetParamsResourceWithInconsistentParamsWrongHl7v2DefaultTemplates()
        {
            var parametersResource = new Parameters();
            parametersResource.Parameter = new List<Parameters.ParameterComponent>();

            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.InputData, Value = new FhirString(GetSampleCCDAMessage()) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.InputDataType, Value = new FhirString("CCDA") });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.TemplateCollectionReference, Value = new FhirString("microsofthealth/hl7v2templates:default") });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.RootTemplate, Value = new FhirString("CCD") });

            return parametersResource;
        }

        private static Parameters GetParamsResourceWithInconsistentParamsWrongCCDADefaultTemplates()
        {
            var parametersResource = new Parameters();
            parametersResource.Parameter = new List<Parameters.ParameterComponent>();

            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.InputData, Value = new FhirString(GetSampleHl7v2Message()) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.InputDataType, Value = new FhirString("Hl7v2") });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.TemplateCollectionReference, Value = new FhirString("microsofthealth/ccdatemplates:default") });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.RootTemplate, Value = new FhirString("ADT_A01") });

            return parametersResource;
        }

        private static Parameters GetParamsResourceWithUnsupportedDataType()
        {
            var parametersResource = new Parameters();
            parametersResource.Parameter = new List<Parameters.ParameterComponent>();

            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.InputData, Value = new FhirString(GetSampleHl7v2Message()) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.InputDataType, Value = new FhirString("invalid") });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.TemplateCollectionReference, Value = new FhirString("test.azurecr.io/testimage:latest") });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.RootTemplate, Value = new FhirString("ADT_A01") });

            return parametersResource;
        }

        private static Parameters GetHl7v2ValidConvertDataParams()
        {
            var parametersResource = new Parameters();
            parametersResource.Parameter = new List<Parameters.ParameterComponent>();

            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.InputData, Value = new FhirString(GetSampleHl7v2Message()) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.InputDataType, Value = new FhirString("Hl7v2") });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.TemplateCollectionReference, Value = new FhirString("test.azurecr.io/testimage:latest") });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.RootTemplate, Value = new FhirString("ADT_A01") });

            return parametersResource;
        }

        private static Parameters GetCCDAValidConvertDataParams()
        {
            var parametersResource = new Parameters();
            parametersResource.Parameter = new List<Parameters.ParameterComponent>();

            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.InputData, Value = new FhirString(GetSampleCCDAMessage()) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.InputDataType, Value = new FhirString("CCDA") });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.TemplateCollectionReference, Value = new FhirString("test.azurecr.io/testimage:latest") });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.RootTemplate, Value = new FhirString("CCD") });

            return parametersResource;
        }

        private static Parameters GetHl7v2ValidConvertDataParamsIgnoreCases()
        {
            var parametersResource = new Parameters();
            parametersResource.Parameter = new List<Parameters.ParameterComponent>();

            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.InputData, Value = new FhirString(GetSampleHl7v2Message()) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.InputDataType, Value = new FhirString("hl7v2") });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.TemplateCollectionReference, Value = new FhirString("test.azurecr.io/testimage:latest") });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.RootTemplate, Value = new FhirString("ADT_A01") });

            return parametersResource;
        }

        private static Parameters GetCCDAValidConvertDataParamsIgnoreCases()
        {
            var parametersResource = new Parameters();
            parametersResource.Parameter = new List<Parameters.ParameterComponent>();

            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.InputData, Value = new FhirString(GetSampleCCDAMessage()) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.InputDataType, Value = new FhirString("ccda") });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.TemplateCollectionReference, Value = new FhirString("test.azurecr.io/testimage:latest") });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.RootTemplate, Value = new FhirString("CCD") });

            return parametersResource;
        }

        private static Parameters GetConvertDataParamsWithCustomTemplateReference(string templateCollectionReference)
        {
            var parametersResource = new Parameters();
            parametersResource.Parameter = new List<Parameters.ParameterComponent>();

            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.InputData, Value = new FhirString(GetSampleHl7v2Message()) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.InputDataType, Value = new FhirString("Hl7v2") });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.TemplateCollectionReference, Value = new FhirString(templateCollectionReference) });
            parametersResource.Parameter.Add(new Parameters.ParameterComponent() { Name = ConvertDataProperties.RootTemplate, Value = new FhirString("ADT_A01") });

            return parametersResource;
        }

        private static string GetSampleHl7v2Message()
        {
            return "MSH|^~\\&|SIMHOSP|SFAC|RAPP|RFAC|20200508131015||ADT^A01|517|T|2.3|||AL||44|ASCII\nEVN|A01|20200508131015|||C005^Whittingham^Sylvia^^^Dr^^^DRNBR^PRSNL^^^ORGDR|\nPID|1|3735064194^^^SIMULATOR MRN^MRN|3735064194^^^SIMULATOR MRN^MRN~2021051528^^^NHSNBR^NHSNMBR||Kinmonth^Joanna^Chelsea^^Ms^^CURRENT||19870624000000|F|||89 Transaction House^Handmaiden Street^Wembley^^FV75 4GJ^GBR^HOME||020 3614 5541^HOME|||||||||C^White - Other^^^||||||||\nPD1|||FAMILY PRACTICE^^12345|\nPV1|1|I|OtherWard^MainRoom^Bed 183^Simulated Hospital^^BED^Main Building^4|28b|||C005^Whittingham^Sylvia^^^Dr^^^DRNBR^PRSNL^^^ORGDR|||CAR|||||||||16094728916771313876^^^^visitid||||||||||||||||||||||ARRIVED|||20200508131015||";
        }

        private static string GetSampleCCDAMessage()
        {
            return "<?xml version=\"1.0\"?><?xml-stylesheet type='text/xsl' href=''?><ClinicalDocument xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns=\"urn:hl7-org:v3\" xmlns:mif=\"urn:hl7-org:v3/mif\" xmlns:voc=\"urn:hl7-org:v3/voc\" xmlns:sdtc=\"urn:hl7-org:sdtc\">    <realmCode code=\"US\"/>    <typeId root=\"2.16.840.1.113883.1.3\" extension=\"POCD_HD000040\"/>    <templateId root=\"2.16.840.1.113883.10.20.22.1.1\"/>    <templateId root=\"2.16.840.1.113883.10.20.22.1.2\"/>    <id root=\"2.16.840.1.113883.3.109\" extension=\"bf6b3a62-4293-47b4-9f14-c8829a156f4b\"/>    <code code=\"34133-9\" displayName=\"SUMMARIZATION OF EPISODE NOTE\"        codeSystem=\"2.16.840.1.113883.6.1\" codeSystemName=\"LOINC\"/>    <title>Continuity of Care Document (C-CDA)</title>    <effectiveTime value=\"20170528150200-0400\"/>    <confidentialityCode code=\"N\" displayName=\"Normal Sharing\" codeSystem=\"2.16.840.1.113883.5.25\"        codeSystemName=\"HL7 Confidentiality\"/>    <languageCode code=\"en-US\"/>    <recordTarget>        <patientRole>            <!-- Here is a public id that has an external meaning based on a root OID that is publically identifiable. -->             <!-- root=\"1.3.6.1.4.1.41179.2.4\" is the assigningAutorityName for                     Direct Trust's Patient/Consumer addresses \"DT.org PATIENT\" -->            <id root=\"1.3.6.1.4.1.41179.2.4\" extension=\"lisarnelson@direct.myphd.us\"                 assigningAutorityName=\"DT.org PATIENT\"/>            <!-- More ids may be used. -->            <!-- Here is the patient's MRN at RVHS  -->            <id root=\"2.16.840.1.113883.1.111.12345\" extension=\"12345-0828\"                assigningAuthorityName=\"River Valley Health Services local patient Medical Record Number\" />            <addr>                <streetAddressLine>1 Happy Valley Road</streetAddressLine>                <city>Westerly</city>                <state>RI</state>                <postalCode>02891</postalCode>                <country nullFlavor=\"UNK\"/>            </addr>            <telecom use=\"WP\" value=\"tel:+1-4013482345\"/>            <telecom use=\"HP\" value=\"tel:+1-4016412345\"/>            <telecom value=\"mailto:lisanelson@gmail.com\"/>            <telecom value=\"mailto:lisarnelson@direct.myphd.us\"/>            <patient>                <name use=\"L\">                    <family>Nelson</family>                    <given qualifier=\"CL\">Lisa</given>                </name>                <administrativeGenderCode code=\"F\" displayName=\"Female\"                    codeSystem=\"2.16.840.1.113883.5.1\" codeSystemName=\"HL7 AdministrativeGender\"/>                <birthTime value=\"19620828\"/>                <maritalStatusCode code=\"M\" displayName=\"Married\" codeSystem=\"2.16.840.1.113883.5.2\"                    codeSystemName=\"HL7 MaritalStatus\"/>                <raceCode code=\"2106-3\" displayName=\"White\" codeSystem=\"2.16.840.1.113883.6.238\"                    codeSystemName=\"CDC Race and Ethnicity\"/>                <ethnicGroupCode code=\"2186-5\" displayName=\"Not Hispanic or Latino\"                    codeSystem=\"2.16.840.1.113883.6.238\" codeSystemName=\"CDC Race and Ethnicity\"/>                <languageCommunication>                    <templateId root=\"2.16.840.1.113883.3.88.11.32.2\"/>                    <languageCode code=\"eng\"/>                    <preferenceInd value=\"true\"/>                </languageCommunication>            </patient>            <providerOrganization>                <!-- This is a public id where the root is registered to indicate the National Provider ID -->                <id root=\"2.16.840.1.113883.4.6\" extension=\"1417947383\"                    assigningAuthorityName=\"National Provider ID\"/>                <!-- This is a public id where the root indicates this is a Provider Direct Address. -->                <!-- root=\"1.3.6.1.4.1.41179.2.1\" is the assigningAutorityName for                     Direct Trust's Covered Entity addresses \"DT.org CE\" -->                <id root=\"1.3.6.1.4.1.41179.2.1\" extension=\"rvhs@rvhs.direct.md\" assigningAutorityName=\"DT.org CE (Covered Entity)\"/>                <!-- By including a root OID attribute for a healthcare organization, you can use this information to                 indicate a patient's MRN id at that organization.-->                <id root=\"2.16.840.1.113883.1.111.12345\"                     assigningAuthorityName=\"River Valley Health Services local patient Medical Record Number\" />                <name>River Valley Health Services</name>                <telecom use=\"WP\" value=\"tel:+1-4015394321\"/>                <telecom use=\"WP\" value=\"mailto:rvhs@rvhs.direct.md\"/>                <addr>                    <streetAddressLine>823 Main Street</streetAddressLine>                    <city>River Valley</city>                    <state>RI</state>                    <postalCode>028321</postalCode>                    <country>US</country>                </addr>            </providerOrganization>        </patientRole>    </recordTarget>    ...    <component>        <structuredBody>         ...         </structuredBody>    </component></ClinicalDocument>";
        }

        private static string GetSampleHl7v2ConvertDataResponse()
        {
            return "{ \"resourceType\": \"Bundle\" }";
        }

        private static string GetSampleCCDAConvertDataResponse()
        {
            return "{ \"resourceType\": \"Bundle\" }";
        }
    }
}
