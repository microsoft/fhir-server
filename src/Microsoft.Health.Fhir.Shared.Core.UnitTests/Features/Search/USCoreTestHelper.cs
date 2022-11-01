// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Newtonsoft.Json.Linq;
using NSubstitute;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Search
{
    public static class USCoreTestHelper
    {
        // JSON Data samples extracted from:
        // - https://www.hl7.org/fhir/allergyintolerance-example.json.html
        // - https://www.hl7.org/fhir/condition-example.json.html
        // - https://www.hl7.org/fhir/DocumentReference-example.json.html
        // - https://www.hl7.org/fhir/Immunization-example.json.html
        // - https://www.hl7.org/fhir/goal-example.json.html

        // XML Data samples extracted from:
        // - https://www.hl7.org/fhir/allergyintolerance-example.json.html
        // - https://www.hl7.org/fhir/condition-example.json.html
        // - https://www.hl7.org/fhir/DocumentReference-example.json.html
        // - https://www.hl7.org/fhir/Immunization-example.json.html
        // - https://www.hl7.org/fhir/goal-example.json.html

        public const string JsonCompliantDataSamplesFileName = "USCoreMissinData-JsonCompliantSamples.json";
        public const string JsonNonCompliantDataSamplesFileName = "USCoreMissinData-JsonNonCompliantSamples.json";
        public const string XmlCompliantDataSamplesFileName = "USCoreMissinData-XmlCompliantSamples.xml";
        public const string XmlNonCompliantDataSamplesFileName = "USCoreMissinData-XmlNonCompliantSamples.xml";

        private static readonly ParserSettings _parserSettings = new ParserSettings() { AcceptUnknownMembers = true, PermissiveParsing = true };
        private static readonly FhirJsonParser _jsonParser = new FhirJsonParser(_parserSettings);
        private static readonly FhirXmlParser _xmlParser = new FhirXmlParser(_parserSettings);

        public static SearchResult GetSearchResult(string fileName)
        {
            string extension = Path.GetExtension(fileName);
            switch (extension)
            {
                case ".json":
                    return GetJsonSearchResult(fileName);
                case ".xml":
                    return GetXmlSearchResult(fileName);
                default:
                    throw new InvalidOperationException($"Invalid extension '{extension}'.");
            }
        }

        private static SearchResult GetXmlSearchResult(string fileName)
        {
            string rawDataElements = GetSamplesFromFile(fileName);
            XmlDocument doc = new XmlDocument();
            doc.LoadXml(rawDataElements);

            List<SearchResultEntry> resultEntries = new List<SearchResultEntry>();
            foreach (XmlNode dataElement in doc.FirstChild.ChildNodes)
            {
                string rawResourceAsXml = dataElement.OuterXml;
                resultEntries.Add(CreateSearchResultEntryFromXml(rawResourceAsXml));
            }

            SearchResult searchResult = new SearchResult(
                resultEntries,
                continuationToken: null,
                sortOrder: null,
                unsupportedSearchParameters: Array.Empty<Tuple<string, string>>());
            return searchResult;
        }

        private static SearchResult GetJsonSearchResult(string fileName)
        {
            string rawDataElements = GetSamplesFromFile(fileName);
            JArray dataElementsArray = JArray.Parse(rawDataElements);

            List<SearchResultEntry> resultEntries = new List<SearchResultEntry>();
            foreach (JToken dataElement in dataElementsArray.Children())
            {
                string rawResourceAsJson = dataElement.ToString();
                resultEntries.Add(CreateSearchResultEntryFromJson(rawResourceAsJson));
            }

            SearchResult searchResult = new SearchResult(
                resultEntries,
                continuationToken: null,
                sortOrder: null,
                unsupportedSearchParameters: Array.Empty<Tuple<string, string>>());
            return searchResult;
        }

        private static SearchResultEntry CreateSearchResultEntryFromXml(string rawResourceAsXml)
        {
            var parsedElement = _xmlParser.Parse(rawResourceAsXml);
            ResourceElement resourceElement = parsedElement.ToResourceElement();

            RawResource rawResource = new RawResource(
                rawResourceAsXml,
                FhirResourceFormat.Xml,
                isMetaSet: false);

            return new SearchResultEntry(
                new ResourceWrapper(
                    resourceElement,
                    rawResource,
                    null,
                    false,
                    null,
                    null,
                    null));
        }

        private static SearchResultEntry CreateSearchResultEntryFromJson(string rawResourceAsJson)
        {
            var parsedElement = _jsonParser.Parse(rawResourceAsJson);
            ResourceElement resourceElement = parsedElement.ToResourceElement();

            RawResource rawResource = new RawResource(
                rawResourceAsJson,
                FhirResourceFormat.Json,
                isMetaSet: false);

            return new SearchResultEntry(
                new ResourceWrapper(
                    resourceElement,
                    rawResource,
                    null,
                    false,
                    null,
                    null,
                    null));
        }

        public static ISearchResultFilter GetSearchResultFilter(bool isUSCoreMissingDataEnabled, bool isSmartUserRequest)
        {
            if (!isUSCoreMissingDataEnabled && !isSmartUserRequest)
            {
                return SearchResultFilter.Default;
            }

            // Setting up Implementation Guides Configuration with "US Core Missing Data" enabled or disabled.
            var implementationGuidesConfiguration = new ImplementationGuidesConfiguration()
            {
                USCore = new USCoreConfiguration()
                {
                    MissingData = isUSCoreMissingDataEnabled,
                },
            };
            IOptions<ImplementationGuidesConfiguration> implementationGuidesConfig = Substitute.For<IOptions<ImplementationGuidesConfiguration>>();
            implementationGuidesConfig.Value.Returns(implementationGuidesConfiguration);

            // Simulating a FHIR Request Context with or without SMART user.
            IFhirRequestContext fhirRequestContext = new FhirRequestContext("foo", "bar", "baz", "foo", requestHeaders: null, responseHeaders: new Dictionary<string, StringValues>());
            fhirRequestContext.AccessControlContext.ApplyFineGrainedAccessControl = isSmartUserRequest;
            RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor = new FhirRequestContextAccessor()
            {
                RequestContext = fhirRequestContext,
            };

            return new SearchResultFilter(implementationGuidesConfig, fhirRequestContextAccessor);
        }

        private static string GetSamplesFromFile(string fileName)
        {
            string resourceName = $"Microsoft.Health.Fhir.Core.UnitTests.TestFiles.{fileName}";

            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException($"Resource not found: '{resourceName}'.");
                }

                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}
