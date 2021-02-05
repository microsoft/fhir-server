// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
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

        public static string GetProvenanceHeader() => GetJson("ProvenanceHeader");

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

        public static ResourceElement GetDefaultTransaction()
        {
            return GetJsonSample("Bundle-Transaction");
        }

        public static ResourceElement GetDefaultConvertDataParameter()
        {
            return GetJsonSample("Parameter-Convert-Data");
        }

        public static Stream GetDefaultConversionTemplates()
        {
            return EmbeddedResourceManager.GetContentAsSteam(EmbeddedResourceSubNamespace, "conversion_templates", "tar.gz");
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
    }
}
