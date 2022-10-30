// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [Trait(Traits.Category, Categories.SmartOnFhir)]
    public sealed class SearchResultFilterTests
    {
        // JSON Data samples extracted from:
        // - https://www.hl7.org/fhir/allergyintolerance-example.json.html
        // - https://www.hl7.org/fhir/condition-example.json.html
        // - https://www.hl7.org/fhir/DocumentReference-example.json.html
        // - https://www.hl7.org/fhir/Immunization-example.json.html
        // - https://www.hl7.org/fhir/goal-example.json.html

        private const string JsonCompliantDataSamplesFileName = "USCoreMissinData-JsonCompliantSamples";

        private static readonly ParserSettings _parserSettings = new ParserSettings() { AcceptUnknownMembers = true, PermissiveParsing = true };
        private static readonly FhirJsonParser _jsonParser = new FhirJsonParser(_parserSettings);
        private static readonly FhirXmlParser _xmlParser = new FhirXmlParser(_parserSettings);

        [Theory]
        [InlineData(true, true)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(false, false)]
        public void WhenFilteringResults_IfNoMissingStatusElements_ThenShowDataAsIs(bool isUSCoreMissingDataEnabled, bool isSmartUserRequest)
        {
            ISearchResultFilter searchResultFilter = GetSearchResultFilter(isUSCoreMissingDataEnabled, isSmartUserRequest);

            string rawDataElements = GetSamplesFromFile(JsonCompliantDataSamplesFileName);
            JArray dataElementsArray = JArray.Parse(rawDataElements);

            List<SearchResultEntry> resultEntries = new List<SearchResultEntry>();
            foreach (JToken dataElement in dataElementsArray.Children())
            {
                string rawResourceAsJson = dataElement.ToString();
                resultEntries.Add(CreateSearchResultEntry(rawResourceAsJson));
            }

            SearchResult searchResult = new SearchResult(
                resultEntries,
                continuationToken: null,
                sortOrder: null,
                unsupportedSearchParameters: Array.Empty<Tuple<string, string>>());

            SearchResult filteredSearchResult = searchResultFilter.Filter(searchResult);

            Assert.Equal(searchResult.Results.Count(), filteredSearchResult.Results.Count());
            Assert.Equal(searchResult.ContinuationToken, filteredSearchResult.ContinuationToken);
            Assert.Equal(searchResult.SortOrder, filteredSearchResult.SortOrder);
            Assert.Equal(searchResult.SearchIssues.Count, filteredSearchResult.SearchIssues.Count);
            Assert.Empty(filteredSearchResult.SearchIssues);
        }

        private static SearchResultEntry CreateSearchResultEntry(string rawResourceAsJson)
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

        private static ISearchResultFilter GetSearchResultFilter(bool isUSCoreMissingDataEnabled, bool isSmartUserRequest)
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
            string resourceName = $"Microsoft.Health.Fhir.Core.UnitTests.TestFiles.{fileName}.json";

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
