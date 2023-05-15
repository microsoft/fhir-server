// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Models;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Tests.Common
{
    public class Samples
    {
        private const string EmbeddedResourceSubNamespace = "TestFiles";

        public const string SampleHl7v2Message = "MSH|^~\\&|AccMgr|1|||20050110045504||ADT^A01|599102|P|2.3||| \nEVN|A01|20050110045502||||| \nPID|1||10006579^^^1^MR^1||DUCK^DONALD^D||19241010|M||1|111 DUCK ST^^FOWL^CA^999990000^^M|1|8885551212|8885551212|1|2||40007716^^^AccMgr^VN^1|123121234|||||||||||NO \nNK1|1|DUCK^HUEY|SO|3583 DUCK RD^^FOWL^CA^999990000|8885552222||Y|||||||||||||| \nPV1|1|I|PREOP^101^1^1^^^S|3|||37^DISNEY^WALT^^^^^^AccMgr^^^^CI|||01||||1|||37^DISNEY^WALT^^^^^^AccMgr^^^^CI|2|40007716^^^AccMgr^VN|4|||||||||||||||||||1||G|||20050110045253|||||| \nGT1|1|8291|DUCK^DONALD^D||111^DUCK ST^^FOWL^CA^999990000|8885551212||19241010|M||1|123121234||||#Cartoon Ducks Inc|111^DUCK ST^^FOWL^CA^999990000|8885551212||PT| \nDG1|1|I9|71596^OSTEOARTHROS NOS-L/LEG ^I9|OSTEOARTHROS NOS-L/LEG ||A| \nIN1|1|MEDICARE|3|MEDICARE|||||||Cartoon Ducks Inc|19891001|||4|DUCK^DONALD^D|1|19241010|111^DUCK ST^^FOWL^CA^999990000|||||||||||||||||123121234A||||||PT|M|111 DUCK ST^^FOWL^CA^999990000|||||8291 \nIN2|1||123121234|Cartoon Ducks Inc|||123121234A|||||||||||||||||||||||||||||||||||||||||||||||||||||||||8885551212 \nIN1|2|NON-PRIMARY|9|MEDICAL MUTUAL CALIF.|PO BOX 94776^^HOLLYWOOD^CA^441414776||8003621279|PUBSUMB|||Cartoon Ducks Inc||||7|DUCK^DONALD^D|1|19241010|111 DUCK ST^^FOWL^CA^999990000|||||||||||||||||056269770||||||PT|M|111^DUCK ST^^FOWL^CA^999990000|||||8291 \nIN2|2||123121234|Cartoon Ducks Inc||||||||||||||||||||||||||||||||||||||||||||||||||||||||||||8885551212 \nIN1|3|SELF PAY|1|SELF PAY|||||||||||5||1\n";

        public const string SampleCcdaMessage = "<?xml version=\"1.0\"?><?xml-stylesheet type='text/xsl' href=''?><ClinicalDocument xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xmlns=\"urn:hl7-org:v3\" xmlns:mif=\"urn:hl7-org:v3/mif\" xmlns:voc=\"urn:hl7-org:v3/voc\" xmlns:sdtc=\"urn:hl7-org:sdtc\">    <realmCode code=\"US\"/>    <typeId root=\"2.16.840.1.113883.1.3\" extension=\"POCD_HD000040\"/>    <templateId root=\"2.16.840.1.113883.10.20.22.1.1\"/>    <templateId root=\"2.16.840.1.113883.10.20.22.1.2\"/>    <id root=\"2.16.840.1.113883.3.109\" extension=\"bf6b3a62-4293-47b4-9f14-c8829a156f4b\"/>    <code code=\"34133-9\" displayName=\"SUMMARIZATION OF EPISODE NOTE\"        codeSystem=\"2.16.840.1.113883.6.1\" codeSystemName=\"LOINC\"/>    <title>Continuity of Care Document (C-CDA)</title>    <effectiveTime value=\"20170528150200-0400\"/>    <confidentialityCode code=\"N\" displayName=\"Normal Sharing\" codeSystem=\"2.16.840.1.113883.5.25\"        codeSystemName=\"HL7 Confidentiality\"/>    <languageCode code=\"en-US\"/>    <recordTarget>        <patientRole>            <!-- Here is a public id that has an external meaning based on a root OID that is publically identifiable. -->             <!-- root=\"1.3.6.1.4.1.41179.2.4\" is the assigningAutorityName for                     Direct Trust's Patient/Consumer addresses \"DT.org PATIENT\" -->            <id root=\"1.3.6.1.4.1.41179.2.4\" extension=\"lisarnelson@direct.myphd.us\"                 assigningAutorityName=\"DT.org PATIENT\"/>            <!-- More ids may be used. -->            <!-- Here is the patient's MRN at RVHS  -->            <id root=\"2.16.840.1.113883.1.111.12345\" extension=\"12345-0828\"                assigningAuthorityName=\"River Valley Health Services local patient Medical Record Number\" />            <addr>                <streetAddressLine>1 Happy Valley Road</streetAddressLine>                <city>Westerly</city>                <state>RI</state>                <postalCode>02891</postalCode>                <country nullFlavor=\"UNK\"/>            </addr>            <telecom use=\"WP\" value=\"tel:+1-4013482345\"/>            <telecom use=\"HP\" value=\"tel:+1-4016412345\"/>            <telecom value=\"mailto:lisanelson@gmail.com\"/>            <telecom value=\"mailto:lisarnelson@direct.myphd.us\"/>            <patient>                <name use=\"L\">                    <family>Nelson</family>                    <given qualifier=\"CL\">Lisa</given>                </name>                <administrativeGenderCode code=\"F\" displayName=\"Female\"                    codeSystem=\"2.16.840.1.113883.5.1\" codeSystemName=\"HL7 AdministrativeGender\"/>                <birthTime value=\"19620828\"/>                <maritalStatusCode code=\"M\" displayName=\"Married\" codeSystem=\"2.16.840.1.113883.5.2\"                    codeSystemName=\"HL7 MaritalStatus\"/>                <raceCode code=\"2106-3\" displayName=\"White\" codeSystem=\"2.16.840.1.113883.6.238\"                    codeSystemName=\"CDC Race and Ethnicity\"/>                <ethnicGroupCode code=\"2186-5\" displayName=\"Not Hispanic or Latino\"                    codeSystem=\"2.16.840.1.113883.6.238\" codeSystemName=\"CDC Race and Ethnicity\"/>                <languageCommunication>                    <templateId root=\"2.16.840.1.113883.3.88.11.32.2\"/>                    <languageCode code=\"eng\"/>                    <preferenceInd value=\"true\"/>                </languageCommunication>            </patient>            <providerOrganization>                <!-- This is a public id where the root is registered to indicate the National Provider ID -->                <id root=\"2.16.840.1.113883.4.6\" extension=\"1417947383\"                    assigningAuthorityName=\"National Provider ID\"/>                <!-- This is a public id where the root indicates this is a Provider Direct Address. -->                <!-- root=\"1.3.6.1.4.1.41179.2.1\" is the assigningAutorityName for                     Direct Trust's Covered Entity addresses \"DT.org CE\" -->                <id root=\"1.3.6.1.4.1.41179.2.1\" extension=\"rvhs@rvhs.direct.md\" assigningAutorityName=\"DT.org CE (Covered Entity)\"/>                <!-- By including a root OID attribute for a healthcare organization, you can use this information to                 indicate a patient's MRN id at that organization.-->                <id root=\"2.16.840.1.113883.1.111.12345\"                     assigningAuthorityName=\"River Valley Health Services local patient Medical Record Number\" />                <name>River Valley Health Services</name>                <telecom use=\"WP\" value=\"tel:+1-4015394321\"/>                <telecom use=\"WP\" value=\"mailto:rvhs@rvhs.direct.md\"/>                <addr>                    <streetAddressLine>823 Main Street</streetAddressLine>                    <city>River Valley</city>                    <state>RI</state>                    <postalCode>028321</postalCode>                    <country>US</country>                </addr>            </providerOrganization>        </patientRole>    </recordTarget>    ...    <component>        <structuredBody>         ...         </structuredBody>    </component></ClinicalDocument>";

        public const string SampleJsonMessage = "{\"PatientId\": 12434, \"MRN\": \"M0R1N2\",\"FirstName\": \"Jerry\", \"LastName\": \"Smith\", \"Phone Number\": [\"1234-5678\", \"1234-5679\"], \"Gender\": \"M\", \"DOB\": \"20010110\"}";

        public const string SampleFhirStu3Message = "{\"resourceType\": \"Patient\", \"id\": 12434, \"animal\": {\"species\": {\"coding\": [ {\"system\": \"http://hl7.org/fhir/animal-species\", \"code\": \"canislf\", \"display\": \"Dog\"}]}}}";

        public const string SampleConvertDataResponse = "{ \"resourceType\": \"Bundle\" }";

        /// <summary>
        /// This returns invalid resource bytes from the json files
        /// </summary>
        public static string GetInvalidResourceJson()
        {
            return GetJson("InvalidObservation");
        }

        /// <summary>
        /// This returns a weight sample from the json files
        /// </summary>
        public static ResourceElement GetDefaultObservation()
        {
            return GetJsonSample("Weight");
        }

        /// <summary>
        /// This returns a patient sample from the json files
        /// </summary>
        public static ResourceElement GetDefaultPatient()
        {
            return GetJsonSample("Patient");
        }

        /// <summary>
        /// This returns a list sample from the json files
        /// </summary>
        public static ResourceElement GetDefaultList()
        {
            return GetJsonSample("list-example-long");
        }

        /// <summary>
        /// This returns a organization sample from the json files
        /// </summary>
        public static ResourceElement GetDefaultOrganization()
        {
            return GetJsonSample("Organization");
        }

        /// <summary>
        /// This returns a medication sample from the json files
        /// </summary>
        public static ResourceElement GetDefaultMedication()
        {
            return GetJsonSample("Medication");
        }

        public static string GetProvenanceHeader() => GetJson("ProvenanceHeader");

        public static ResourceElement GetDefaultCoverage() => GetJsonSample("Coverage");

        public static ResourceElement GetBatchWithDuplicatedItems()
        {
            var batch = GetJsonSample("Bundle-BatchWithDuplicatedItems").ToPoco<Bundle>();

            // Make the criteria unique so that the tests behave consistently
            var createGuid = Guid.NewGuid().ToString();
            batch.Entry[0].Request.IfNoneExist = batch.Entry[0].Request.IfNoneExist + createGuid;
            var createPatient = (Patient)batch.Entry[0].Resource;
            createPatient.Identifier[0].Value = createPatient.Identifier[0].Value + createGuid;

            var updateIdGuid = Guid.NewGuid().ToString();
            batch.Entry[1].Request.Url = batch.Entry[1].Request.Url + updateIdGuid;
            batch.Entry[1].FullUrl = batch.Entry[1].FullUrl + updateIdGuid;
            var updateIdPatient = (Patient)batch.Entry[1].Resource;
            updateIdPatient.Id = updateIdPatient.Id + updateIdGuid;
            batch.Entry[2].Request.Url = batch.Entry[2].Request.Url + updateIdGuid;
            batch.Entry[2].FullUrl = batch.Entry[2].FullUrl + updateIdGuid;
            var updateIdIfMatchPatient = (Parameters)batch.Entry[2].Resource;
            updateIdIfMatchPatient.Id = updateIdIfMatchPatient.Id + updateIdGuid;

            return batch.ToResourceElement();
        }

        public static ResourceElement GetDefaultBatch()
        {
            var batch = GetJsonSample("Bundle-Batch").ToPoco<Bundle>();

            // Make the criteria unique so that the tests behave consistently
            var createGuid = Guid.NewGuid().ToString();
            batch.Entry[1].Request.IfNoneExist = batch.Entry[1].Request.IfNoneExist + createGuid;
            var createPatient = (Patient)batch.Entry[1].Resource;
            createPatient.Identifier[0].Value = createPatient.Identifier[0].Value + createGuid;

            var updateIdGuid = Guid.NewGuid().ToString();
            batch.Entry[2].Request.Url = batch.Entry[2].Request.Url + updateIdGuid;
            batch.Entry[2].FullUrl = batch.Entry[2].FullUrl + updateIdGuid;
            var updateIdPatient = (Patient)batch.Entry[2].Resource;
            updateIdPatient.Id = updateIdPatient.Id + updateIdGuid;
            batch.Entry[4].Request.Url = batch.Entry[4].Request.Url + updateIdGuid;
            batch.Entry[4].FullUrl = batch.Entry[4].FullUrl + updateIdGuid;
            var updateIdIfMatchPatient = (Patient)batch.Entry[4].Resource;
            updateIdIfMatchPatient.Id = updateIdIfMatchPatient.Id + updateIdGuid;

            var updateIdentifierGuid = Guid.NewGuid().ToString();
            batch.Entry[3].Request.Url = batch.Entry[3].Request.Url + updateIdentifierGuid;
            var updatePatient = (Patient)batch.Entry[3].Resource;
            updatePatient.Identifier[0].Value = updatePatient.Identifier[0].Value + updateIdentifierGuid;

            return batch.ToResourceElement();
        }

        public static ResourceElement GetTransactionBundleWithValidEntries()
        {
            var batch = Samples.GetJsonSample("Bundle-TransactionWithValidBundleEntry").ToPoco<Bundle>();

            // Make the criteria unique so that the tests behave consistently
            var createGuid = Guid.NewGuid().ToString();
            batch.Entry[1].Request.IfNoneExist = batch.Entry[1].Request.IfNoneExist + createGuid;
            var createPatient = (Patient)batch.Entry[1].Resource;
            createPatient.Identifier[0].Value = createPatient.Identifier[0].Value + createGuid;

            var updateIdentifierGuid = Guid.NewGuid().ToString();
            batch.Entry[3].Request.Url = batch.Entry[3].Request.Url + updateIdentifierGuid;
            var updatePatient = (Patient)batch.Entry[3].Resource;
            updatePatient.Identifier[0].Value = updatePatient.Identifier[0].Value + updateIdentifierGuid;

            return batch.ToResourceElement();
        }

        public static ResourceElement GetDefaultTransaction()
        {
            return GetJsonSample("Bundle-Transaction");
        }

        public static ResourceElement GetDefaultConvertDataParameter()
        {
            return GetJsonSample("Parameter-Convert-Data");
        }

        /// <summary>
        /// Gets back a resource from a json sample file.
        /// </summary>
        /// <param name="fileName">The JSON filename, omit the extension</param>
        public static ResourceElement GetJsonSample(string fileName)
        {
            var fhirSource = GetJson(fileName);
            var parser = new Hl7.Fhir.Serialization.FhirJsonParser();
            return parser.Parse<Resource>(fhirSource).ToTypedElement().ToResourceElement();
        }

        /// <summary>
        /// Gets back a resource from a json sample file.
        /// </summary>
        /// <typeparam name="T">The resource type.</typeparam>
        /// <param name="fileName">The JSON filename, omit the extension</param>
        public static T GetJsonSample<T>(string fileName)
        {
            var json = GetJson(fileName);
            if (typeof(Resource).IsAssignableFrom(typeof(T)))
            {
                var parser = new Hl7.Fhir.Serialization.FhirJsonParser();
                return (T)(object)parser.Parse(json, typeof(T));
            }

            return JsonConvert.DeserializeObject<T>(json);
        }

        /// <summary>
        /// Gets back a resource from a xml sample file.
        /// </summary>
        /// <param name="fileName">The XML filename, omit the extension</param>
        public static Resource GetXmlSample(string fileName)
        {
            var fhirSource = GetXml(fileName);
            var parser = new Hl7.Fhir.Serialization.FhirXmlParser();
            return parser.Parse<Resource>(fhirSource);
        }

        /// <summary>
        /// Gets back a the string from a sample file
        /// </summary>
        /// <param name="fileName">The JSON filename, omit the extension</param>
        public static string GetJson(string fileName)
        {
            return EmbeddedResourceManager.GetStringContent(EmbeddedResourceSubNamespace, fileName, "json");
        }

        public static string GetXml(string fileName)
        {
            return EmbeddedResourceManager.GetStringContent(EmbeddedResourceSubNamespace, fileName, "xml");
        }

        public static string GetNdJson(string fileName)
        {
            return EmbeddedResourceManager.GetStringContent(EmbeddedResourceSubNamespace, fileName, "ndjson");
        }
    }
}
