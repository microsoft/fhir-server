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
        public async Task GivenAnIncludeSearchExpressionWithSimpleSearchAndCount_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&_revinclude=DiagnosticReport:result&code=429858000&_count=1";

            Bundle bundle = await Client.SearchAsync(ResourceType.Observation, query);

            ValidateBundle(
                bundle,
                Fixture.SmithSnomedDiagnosticReport,
                Fixture.SmithSnomedObservation,
                Fixture.TrumanSnomedDiagnosticReport);

            ValidateSearchEntryMode(bundle, ResourceType.Observation);

            // todo bug: zzz fix next link issue
            /* bundle = await Client.SearchAsync(bundle.NextLink.ToString());

            ValidateBundle(
                bundle,
                Fixture.TrumanSnomedDiagnosticReport,
                Fixture.TrumanSnomedObservation);

            ValidateSearchEntryMode(bundle, ResourceType.Observation);
            */
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

        // todo bug: fix multiple revincludes
        /*
        [Fact]
        public async Task GivenAnIncludeSearchExpressionWithMultipleIncludes_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&_revinclude=DiagnosticReport:result&_revinclude=Observation:patient&code=429858000";

            Bundle bundle = await Client.SearchAsync(ResourceType.Patient, query);

            ValidateBundle(
                bundle,
                Fixture.SmithSnomedDiagnosticReport,
                Fixture.SmithPatient,
                Fixture.SmithSnomedObservation,
                Fixture.TrumanSnomedDiagnosticReport,
                Fixture.TrumanPatient,
                Fixture.TrumanSnomedObservation);

            ValidateSearchEntryMode(bundle, ResourceType.Patient);
        }*/

        /*
        [Fact]
        public async Task GivenAnIncludeSearchExpressionWithMultipleDenormalizedParametersAndTableParameters_WhenSearched_ThenCorrectBundleShouldBeReturned()
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
            string query = $"_tag={Fixture.Tag}&_revinclude=DiagnosticReport:patient:Patient&_revinclude=DiagnosticReport:result:Observation&code=429858000&_lastUpdated=lt{lastUpdated}";

            Bundle bundle = await Client.SearchAsync(ResourceType.DiagnosticReport, query);

            ValidateBundle(
                bundle,
                Fixture.SmithSnomedDiagnosticReport,
                Fixture.SmithPatient,
                Fixture.SmithSnomedObservation,
                Fixture.TrumanSnomedDiagnosticReport,
                Fixture.TrumanPatient,
                Fixture.TrumanSnomedObservation);

            ValidateSearchEntryMode(bundle, ResourceType.DiagnosticReport);

            // delete the extra entry added
            await Fixture.TestFhirClient.DeleteAsync(newDiagnosticReportResponse.Resource);
        }

        [Fact]
        public async Task GivenAnIncludeSearchExpressionWithNoTargetType_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&_revinclude=Observation:performer";

            Bundle bundle = await Client.SearchAsync(ResourceType.Observation, query);

            ValidateBundle(
                bundle,
                Fixture.AdamsLoincObservation,
                Fixture.SmithLoincObservation,
                Fixture.SmithSnomedObservation,
                Fixture.TrumanLoincObservation,
                Fixture.TrumanSnomedObservation,
                Fixture.Practitioner,
                Fixture.Organization);

            ValidateSearchEntryMode(bundle, ResourceType.Observation);
        } */

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
