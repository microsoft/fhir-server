// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

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
        /// This returns a organization sample from the json files
        /// </summary>
        public static ResourceElement GetDefaultOrganization()
        {
            return GetJsonSample("Organization");
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
