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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Api.Features.Bundle;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Filters;
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

        public static IBundleFactory GetBundleFactory(bool isSmartUserRequest)
        {
            RequestContextAccessor<IFhirRequestContext> requestAccessor = GetRequestContext(isSmartUserRequest);
            UrlResolver urlResolver = CreateUrlResolver(requestAccessor);
            IBundleFactory bundleFactory = new BundleFactory(urlResolver, requestAccessor, NullLogger<BundleFactory>.Instance);

            return bundleFactory;
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

        public static IDataResourceFilter GetDataResourceFilter(bool isUSCoreMissingDataEnabled, bool isSmartUserRequest)
        {
            return new DataResourceFilter(GetMissingDataFilterCriteria(isUSCoreMissingDataEnabled, isSmartUserRequest));
        }

        public static MissingDataFilterCriteria GetMissingDataFilterCriteria(bool isUSCoreMissingDataEnabled, bool isSmartUserRequest)
        {
            if (!isUSCoreMissingDataEnabled && !isSmartUserRequest)
            {
                return MissingDataFilterCriteria.Default;
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
            implementationGuidesConfig.Value.Returns<ImplementationGuidesConfiguration>(implementationGuidesConfiguration);

            // Simulating a FHIR Request Context with or without SMART user.
            RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor = GetRequestContext(isSmartUserRequest);

            return new MissingDataFilterCriteria(implementationGuidesConfig, fhirRequestContextAccessor);
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

        private static RequestContextAccessor<IFhirRequestContext> GetRequestContext(bool isSmartUserRequest)
        {
            IFhirRequestContext fhirRequestContext = new FhirRequestContext("foo", "bar", "baz", "foo", requestHeaders: null, responseHeaders: new Dictionary<string, StringValues>());
            fhirRequestContext.AccessControlContext.ApplyFineGrainedAccessControl = isSmartUserRequest;
            fhirRequestContext.RouteName = "rush";
            RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor = new FhirRequestContextAccessor()
            {
                RequestContext = fhirRequestContext,
            };

            return fhirRequestContextAccessor;
        }

        private static UrlResolver CreateUrlResolver(RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor)
        {
            IUrlHelperFactory urlHelperFactory = Substitute.For<IUrlHelperFactory>();
            IHttpContextAccessor httpContextAccessor = Substitute.For<IHttpContextAccessor>();
            IActionContextAccessor actionContextAccessor = Substitute.For<IActionContextAccessor>();
            IBundleHttpContextAccessor bundleHttpContextAccessor = Substitute.For<IBundleHttpContextAccessor>();
            IUrlHelper urlHelper = Substitute.For<IUrlHelper>();

            var httpContext = new DefaultHttpContext();
            var actionContext = new ActionContext();

            const string scheme = "scheme";
            const string host = "test";

            httpContextAccessor.HttpContext.Returns(httpContext);

            httpContext.Request.Scheme = scheme;
            httpContext.Request.Host = new HostString(host);

            actionContextAccessor.ActionContext.Returns(actionContext);

            urlHelper.RouteUrl(Arg.Do<UrlRouteContext>(_ => { }));
            urlHelperFactory.GetUrlHelper(actionContext).Returns(urlHelper);
            urlHelper.RouteUrl(Arg.Any<UrlRouteContext>()).Returns($"{scheme}://{host}");

            bundleHttpContextAccessor.HttpContext.Returns((HttpContext)null);

            return new UrlResolver(
                fhirRequestContextAccessor,
                urlHelperFactory,
                httpContextAccessor,
                actionContextAccessor,
                bundleHttpContextAccessor);
        }
    }
}
