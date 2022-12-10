// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
    public sealed class ChainingAndSortTests : ChainingSortAndSearchValidationTestFixture
    {
        public ChainingAndSortTests(HttpIntegrationTestFixture fixture)
            : base(fixture)
        {
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAChainedSearchPattern_WhenSearched_ThenCompareTheResultsWithDifferentVariationsOfSortingExpressions()
        {
            await IngestBundleWithTestDataAsync(CancellationToken.None);

            const int expectedNumberOfEntriesInFirstPage = 10;          // Max number of entries in the first page.
            const int expectedNumberOfLinks = 2;                        // Expected number of pages/links.
            const int totalNumberOfFilteredHealthcareServices = 13;     // Expected number of health care services when filters are applied.

            // Check if all HealthcareServices were properly ingested.
            Bundle bundleAllHealthcaseServices = await Client.SearchAsync(ResourceType.HealthcareService, "_total=accurate");
            Assert.Equal(expectedNumberOfEntriesInFirstPage, bundleAllHealthcaseServices.Entry.Count);
            Assert.Equal(expectedNumberOfLinks, bundleAllHealthcaseServices.Link.Count);

            string commonQuery = "name:missing=false&_has:PractitionerRole:service:practitioner=2ec3586b-9454-4c7f-8eaf-7a0e64cecf17&active:not=false&location:missing=false&_has:PractitionerRole:service:active=true";

            // Search for a set of Healthcare Services using chain queries and adittional filtering.
            Bundle bundleWithNoSort = await Client.SearchAsync(ResourceType.HealthcareService, commonQuery);
            Assert.Equal(expectedNumberOfEntriesInFirstPage, bundleWithNoSort.Entry.Count);
            Assert.Equal(expectedNumberOfLinks, bundleWithNoSort.Link.Count);

            // [ Reuse the first query ] + Sort records by name.
            Bundle bundleWithSort = await Client.SearchAsync(ResourceType.HealthcareService, commonQuery + "&_sort=name");
            Assert.Equal(expectedNumberOfEntriesInFirstPage, bundleWithSort.Entry.Count);
            Assert.Equal(expectedNumberOfLinks, bundleWithSort.Link.Count);

            // [ Reuse the first query ] + Return only the total count of records.
            Bundle bundleWithNoSortAndSummaryCount = await Client.SearchAsync(ResourceType.HealthcareService, commonQuery + "&_summary=count");
            Assert.Empty(bundleWithNoSortAndSummaryCount.Entry);
            Assert.Empty(bundleWithNoSortAndSummaryCount.Link);
            Assert.Equal(totalNumberOfFilteredHealthcareServices, bundleWithNoSortAndSummaryCount.Total.Value);

            // [ Reuse the first query ] + Return only the total count of records (with additional _sort expression that will be ignored internally).
            Bundle bundleWithSortAndSummaryCount = await Client.SearchAsync(ResourceType.HealthcareService, commonQuery + "&_sort=name&_summary=count");
            Assert.Empty(bundleWithSortAndSummaryCount.Entry);
            Assert.Empty(bundleWithSortAndSummaryCount.Link);
            Assert.Equal(totalNumberOfFilteredHealthcareServices, bundleWithSortAndSummaryCount.Total.Value);

            // [ Reuse the first query ] + Get total number of records returned.
            Bundle bundleWithNoSortAndTotalAccurate = await Client.SearchAsync(ResourceType.HealthcareService, commonQuery + "&_total=accurate");
            Assert.Equal(expectedNumberOfEntriesInFirstPage, bundleWithNoSortAndTotalAccurate.Entry.Count);
            Assert.Equal(expectedNumberOfLinks, bundleWithNoSortAndTotalAccurate.Link.Count);
            Assert.Equal(totalNumberOfFilteredHealthcareServices, bundleWithNoSortAndTotalAccurate.Total.Value);

            // [ Reuse the first query ] + Sort records by name and get total number of records returned.
            Bundle bundleWithSortAndTotalAccurate = await Client.SearchAsync(ResourceType.HealthcareService, commonQuery + "&_sort=name&_total=accurate");
            Assert.Equal(expectedNumberOfEntriesInFirstPage, bundleWithSortAndTotalAccurate.Entry.Count);
            Assert.Equal(expectedNumberOfLinks, bundleWithSortAndTotalAccurate.Link.Count);
            Assert.Equal(totalNumberOfFilteredHealthcareServices, bundleWithSortAndTotalAccurate.Total.Value);
        }

        private async Task IngestBundleWithTestDataAsync(CancellationToken cancellationToken)
        {
            IReadOnlyList<HealthRecordIdentifier> healthRecordIdentifiers = await GetHealthRecordIdentifiersAsync(cancellationToken);

            var healthCareServices = healthRecordIdentifiers.Where(x => x.ResourceType == ResourceType.HealthcareService.ToString());

            const int totalNumberOfHealthcareServices = 15; // Total number of healthcare services ingested.
            Assert.Equal(totalNumberOfHealthcareServices, healthCareServices.Count());
        }
    }
}
