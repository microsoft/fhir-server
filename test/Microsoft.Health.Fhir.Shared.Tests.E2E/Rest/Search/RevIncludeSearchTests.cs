// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Web;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Shared.Tests.E2E.Rest.Search;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
    public class RevIncludeSearchTests : SearchTestsBase<RevIncludeSearchTestFixture>
    {
        public RevIncludeSearchTests(RevIncludeSearchTestFixture fixture)
            : base(fixture)
        {
        }

        [Fact]
        public async Task GivenARevIncludeSearchExpression_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            // Note: this test is very similar to its matching test in the 'include' part.
            // where the quering direction is different

            // Delete all locations before starting the test.
            await Client.DeleteAllResources(ResourceType.Location);
            await Client.DeleteAllResources(ResourceType.Organization);

            // Creating a Location which refers an Organization
            var organizationResponse = await Client.CreateAsync(new Organization());

            var locationResponse = await Client.CreateAsync(new Location
            {
                ManagingOrganization = new ResourceReference($"Organization/{organizationResponse.Resource.Id}"),
            });

            // Ask for reverse include to get all Locations which reference an org
            string query = $"_revinclude=Location:organization";

            Bundle bundle = await Client.SearchAsync(ResourceType.Organization, query);

            ValidateBundle(
                bundle,
                locationResponse.Resource,
                organizationResponse.Resource);

            ValidateSearchEntryMode(bundle, ResourceType.Organization);

            // ensure that the included resources are not counted
            // todo: issue xyz,fix support for summary count
            // bundle = await Client.SearchAsync(ResourceType.Organization, $"{query}&_summary=count");
            // Assert.Equal(1, bundle.Total);

            // ensure that the included resources are not counted when _total is specified and the results fit in a single bundle.
            bundle = await Client.SearchAsync(ResourceType.Organization, $"{query}&_total=accurate");
            Assert.Equal(1, bundle.Total);
        }

        [Fact]
        public async Task GivenARevIncludeSearchExpression_WhenSearchedWithPost_ThenCorrectBundleShouldBeReturned()
        {
            // Delete all locations and orgs before starting the test.
            await Client.DeleteAllResources(ResourceType.Location);
            await Client.DeleteAllResources(ResourceType.Organization);

            var organizationResponse = await Client.CreateAsync(new Organization());

            var locationResponse = await Client.CreateAsync(new Location
            {
                ManagingOrganization = new ResourceReference($"Organization/{organizationResponse.Resource.Id}"),
            });

            Bundle bundle = await Client.SearchPostAsync(ResourceType.Organization.ToString(), default, ("_revinclude", "Location:organization"));

            ValidateBundle(
                bundle,
                locationResponse.Resource,
                organizationResponse.Resource);

            ValidateSearchEntryMode(bundle, ResourceType.Organization);
        }

        [Fact]
        public async Task GivenARevIncludeSearchExpressionWithSimpleSearch_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&_revinclude=DiagnosticReport:result&code=429858000";

            Bundle bundle = await Client.SearchAsync(ResourceType.Observation, query);

            ValidateBundle(
                bundle,
                Fixture.SmithSnomedDiagnosticReport,
                Fixture.SmithSnomedObservation,
                Fixture.TrumanSnomedDiagnosticReport,
                Fixture.TrumanSnomedObservation);

            ValidateSearchEntryMode(bundle, ResourceType.Observation);
        }

        [Fact]
        public async Task GivenARevIncludeSearchExpressionWithSimpleSearchAndCount_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&_revinclude=DiagnosticReport:result&code=429858000&_count=1";

            Bundle bundle = await Client.SearchAsync(ResourceType.Observation, query);

            // Note: The way fhir server works with paging, it asks for 1 extra match item
            // to avoid additional call. this also gives us other includes.
            // fhir will exclude that extra match but the extra include will be
            // passed to the client (and will be ignoredß)
            ValidateBundle(
                bundle,
                Fixture.SmithSnomedDiagnosticReport,
                Fixture.SmithSnomedObservation,
                Fixture.TrumanSnomedDiagnosticReport);

            ValidateSearchEntryMode(bundle, ResourceType.Observation);

            bundle = await Client.SearchAsync(bundle.NextLink.ToString());

            ValidateBundle(
                bundle,
                Fixture.TrumanSnomedDiagnosticReport,
                Fixture.TrumanSnomedObservation);

            ValidateSearchEntryMode(bundle, ResourceType.Observation);
        }

        [Fact]
        public async Task GivenAnIncludeSearchExpressionWithWildcard_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&_revinclude=DiagnosticReport:*&code=429858000";

            Bundle bundle = await Client.SearchAsync(ResourceType.Observation, query);

            ValidateBundle(
                bundle,
                Fixture.SmithSnomedDiagnosticReport,
                Fixture.SmithSnomedObservation,
                Fixture.TrumanSnomedDiagnosticReport,
                Fixture.TrumanSnomedObservation);

            ValidateSearchEntryMode(bundle, ResourceType.Observation);
        }

        [Fact]
        public async Task GivenARevIncludeSearchExpressionWithMultipleIncludes_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&_revinclude=DiagnosticReport:result&_revinclude=Observation:patient&family=Truman";

            Bundle bundle = await Client.SearchAsync(ResourceType.Patient, query);

            ValidateBundle(
                bundle,
                Fixture.TrumanPatient,
                Fixture.TrumanSnomedDiagnosticReport,
                Fixture.TrumanSnomedObservation,
                Fixture.TrumanLoincDiagnosticReport,
                Fixture.TrumanLoincObservation);

            ValidateSearchEntryMode(bundle, ResourceType.Patient);
        }

        [Fact]
        public async Task GivenARevIncludeSearchExpressionWithMultipleDenormalizedParametersAndTableParameters_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            var newDiagnosticReportResponse = await Fixture.TestFhirClient.CreateAsync(
                new DiagnosticReport
                {
                    Meta = new Meta { Tag = new List<Coding> { new Coding("testTag", Fixture.Tag) } },
                    Status = DiagnosticReport.DiagnosticReportStatus.Final,
                    Code = new CodeableConcept("http://snomed.info/sct", "429858000"),
                    Subject = new ResourceReference($"Patient/{Fixture.TrumanPatient.Id}"),
                    Result = new List<ResourceReference> { new ResourceReference($"Observation/{Fixture.TrumanSnomedObservation.Id}") },
                });

            // Format the time to fit yyyy-MM-ddTHH:mm:ss.fffffffzzz, and encode its special characters.
            string lastUpdated = HttpUtility.UrlEncode($"{Fixture.PatientGroup.Meta.LastUpdated:o}");
            string query = $"_tag={Fixture.Tag}&_revinclude=DiagnosticReport:result&code=429858000&_lastUpdated=lt{lastUpdated}";

            Bundle bundle = await Client.SearchAsync(ResourceType.Observation, query);

            ValidateBundle(
                bundle,
                Fixture.SmithSnomedDiagnosticReport,
                Fixture.SmithSnomedObservation,
                Fixture.TrumanSnomedDiagnosticReport,
                Fixture.TrumanSnomedObservation,
                newDiagnosticReportResponse.Resource);

            ValidateSearchEntryMode(bundle, ResourceType.Observation);

            // delete the extra entry added
            await Fixture.TestFhirClient.DeleteAsync(newDiagnosticReportResponse.Resource);
        }

        [Fact]
        public async Task GivenARevIncludeSearchExpressionWithWithNoReferences_WhenSearched_ThenCorrectBundleWithOnlyMatchesShouldBeReturned()
        {
            // looking for an appointment referencing a Patient, however this kind of reference was
            // not created in this fixture.
            string query = $"_tag={Fixture.Tag}&_revinclude=Appointment:actor";

            Bundle bundle = await Client.SearchAsync(ResourceType.Patient, query);

            ValidateBundle(
                bundle,
                Fixture.SmithPatient,
                Fixture.TrumanPatient,
                Fixture.AdamsPatient);

            ValidateSearchEntryMode(bundle, ResourceType.Patient);
        }

        private static void ValidateSearchEntryMode(Bundle bundle, ResourceType matchResourceType)
        {
            foreach (Bundle.EntryComponent entry in bundle.Entry)
            {
                var searchEntryMode = entry.Resource.ResourceType == matchResourceType ? Bundle.SearchEntryMode.Match : Bundle.SearchEntryMode.Include;
                Assert.Equal(searchEntryMode, entry.Search.Mode);
            }
        }
    }
}
