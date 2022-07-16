// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using Hl7.Fhir.Model;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    [Trait(Traits.Category, Categories.Search)]
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
    public sealed class ChainingAndSortTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private readonly TestFhirClient _client;

        public ChainingAndSortTests(HttpIntegrationTestFixture fixture)
        {
            _client = fixture.TestFhirClient;
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task TestSearchExecution_WithChainingAndSortOperators()
        {
            string requestBundleAsString = Samples.GetJson("Bundle-ChainingAndSortSearchValidation");
            var parser = new Hl7.Fhir.Serialization.FhirJsonParser();
            var requestBundle = parser.Parse<Bundle>(requestBundleAsString);

            using FhirResponse<Bundle> fhirResponse = await _client.PostBundleAsync(requestBundle);
            Assert.NotNull(fhirResponse);
            Assert.Equal(HttpStatusCode.OK, fhirResponse.StatusCode);

            string commonQuery = "name:missing=false&_has:PractitionerRole:service:practitioner=2ec3586b-9454-4c7f-8eaf-7a0e64cecf17&active:not=false&location:missing=false&_has:PractitionerRole:service:active=true";

            const int expectedNumberOfEntriesInFirstPage = 10;
            const int totalExpectedNumberOfEntries = 13;
            const int expectedNumberOfLinks = 2;

            Bundle bundleWithNoSort = await _client.SearchAsync(ResourceType.HealthcareService, commonQuery);
            Assert.Equal(expectedNumberOfEntriesInFirstPage, bundleWithNoSort.Entry.Count);
            Assert.Equal(expectedNumberOfLinks, bundleWithNoSort.Link.Count);

            Bundle bundleWithSort = await _client.SearchAsync(ResourceType.HealthcareService, commonQuery + "&_sort=name");
            Assert.Equal(expectedNumberOfEntriesInFirstPage, bundleWithSort.Entry.Count);
            Assert.Equal(expectedNumberOfLinks, bundleWithSort.Link.Count);

            Bundle bundleWithNoSortAndSummaryCount = await _client.SearchAsync(ResourceType.HealthcareService, commonQuery + "&_summary=count");
            Assert.Empty(bundleWithNoSortAndSummaryCount.Entry);
            Assert.Empty(bundleWithNoSortAndSummaryCount.Link);
            Assert.Equal(totalExpectedNumberOfEntries, bundleWithNoSortAndSummaryCount.Total.Value);

            Bundle bundleWithSortAndSummaryCount = await _client.SearchAsync(ResourceType.HealthcareService, commonQuery + "&_sort=name&_summary=count");
            Assert.Empty(bundleWithSortAndSummaryCount.Entry);
            Assert.Empty(bundleWithSortAndSummaryCount.Link);
            Assert.Equal(totalExpectedNumberOfEntries, bundleWithSortAndSummaryCount.Total.Value);

            Bundle bundleWithNoSortAndTotalAccurate = await _client.SearchAsync(ResourceType.HealthcareService, commonQuery + "&_total=accurate");
            Assert.Equal(expectedNumberOfEntriesInFirstPage, bundleWithNoSortAndTotalAccurate.Entry.Count);
            Assert.Equal(expectedNumberOfLinks, bundleWithNoSortAndTotalAccurate.Link.Count);
            Assert.Equal(totalExpectedNumberOfEntries, bundleWithNoSortAndTotalAccurate.Total.Value);

            Bundle bundleWithSortAndTotalAccurate = await _client.SearchAsync(ResourceType.HealthcareService, commonQuery + "&_sort=name&_total=accurate");
            Assert.Equal(expectedNumberOfEntriesInFirstPage, bundleWithSortAndTotalAccurate.Entry.Count);
            Assert.Equal(expectedNumberOfLinks, bundleWithSortAndTotalAccurate.Link.Count);
            Assert.Equal(totalExpectedNumberOfEntries, bundleWithSortAndTotalAccurate.Total.Value);
        }
    }
}
