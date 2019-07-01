// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Shared.Tests.E2E.Rest.Search;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
    public class IncludeSearchTests : SearchTestsBase<IncludeSearchTestFixture>
    {
        public IncludeSearchTests(IncludeSearchTestFixture fixture)
            : base(fixture)
        {
        }

        [Fact]
        public async Task GivenAnIncludeSearchExpression_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            // Delete all patients before starting the test.
            await Client.DeleteAllResources(ResourceType.Location);
            var organizationResponse = await Client.CreateAsync(new Organization());

            var locationResponse = await Client.CreateAsync(new Location
            {
                ManagingOrganization = new ResourceReference($"Organization/{organizationResponse.Resource.Id}"),
            });

            string query = $"_include=Location:Organization:organization";

            Bundle bundle = await Client.SearchAsync(ResourceType.Location, query);

            ValidateBundle(
                bundle,
                organizationResponse.Resource,
                locationResponse.Resource);
        }

        [Fact]
        public async Task GivenAnIncludeSearchExpressionWithMultipleDenormalizedParameters_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            // Delete all patients before starting the test.
            await Client.DeleteAllResources(ResourceType.Location);
            var organizationResponse = await Client.CreateAsync(new Organization());

            var locationResponse = await Client.CreateAsync(new Location
            {
                ManagingOrganization = new ResourceReference($"Organization/{organizationResponse.Resource.Id}"),
            });

            var locationResponse2 = await Client.CreateAsync(new Location
            {
                ManagingOrganization = new ResourceReference($"Organization/{organizationResponse.Resource.Id}"),
            });

            string query = $"_include=Location:Organization:organization&_lastUpdated=lt{locationResponse2.Resource.Meta.LastUpdated:o}";

            Bundle bundle = await Client.SearchAsync(ResourceType.Location, query);

            ValidateBundle(
                bundle,
                organizationResponse.Resource,
                locationResponse.Resource);
        }

        [Fact]
        public async Task GivenAnIncludeSearchExpressionWithSimpleSearch_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&_include=DiagnosticReport:Patient:patient&code=429858000";

            Bundle bundle = await Client.SearchAsync(ResourceType.DiagnosticReport, query);

            ValidateBundle(
                bundle,
                Fixture.SmithSnomedDiagnosticReport,
                Fixture.SmithPatient,
                Fixture.TrumanSnomedDiagnosticReport,
                Fixture.TrumanPatient);
        }

        [Fact]
        public async Task GivenAnIncludeSearchExpressionWithWildcard_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&_include=DiagnosticReport:*&code=429858000";

            Bundle bundle = await Client.SearchAsync(ResourceType.DiagnosticReport, query);

            ValidateBundle(
                bundle,
                Fixture.SmithSnomedDiagnosticReport,
                Fixture.SmithPatient,
                Fixture.SmithSnomedObservation,
                Fixture.TrumanSnomedDiagnosticReport,
                Fixture.TrumanPatient,
                Fixture.TrumanSnomedObservation);
        }

        [Fact]
        public async Task GivenAnIncludeSearchExpressionWithMultipleIncludes_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&_include=DiagnosticReport:Patient:patient&_include=DiagnosticReport:Observation:result&code=429858000";

            Bundle bundle = await Client.SearchAsync(ResourceType.DiagnosticReport, query);

            ValidateBundle(
                bundle,
                Fixture.SmithSnomedDiagnosticReport,
                Fixture.SmithPatient,
                Fixture.SmithSnomedObservation,
                Fixture.TrumanSnomedDiagnosticReport,
                Fixture.TrumanPatient,
                Fixture.TrumanSnomedObservation);
        }

        [Fact(Skip = "https://github.com/microsoft/fhir-server/issues/563")]
        public async Task GivenAnIncludeSearchExpressionWithMultipleDenormalizedParametersAndTableParameters_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            var newDiagnosticReport = new DiagnosticReport
            {
                Meta = new Meta { Tag = new List<Coding> { new Coding("testTag", Fixture.Tag) } },
                Status = DiagnosticReport.DiagnosticReportStatus.Final,
                Code = new CodeableConcept("http://snomed.info/sct", "429858000"),
                Subject = new ResourceReference($"Patient/{Fixture.TrumanPatient.Id}"),
                Result = new List<ResourceReference> { new ResourceReference($"Observation/{Fixture.TrumanSnomedObservation.Id}") },
            };

            newDiagnosticReport = (await Fixture.FhirClient.CreateAsync(newDiagnosticReport)).Resource;

            string query = $"_tag={Fixture.Tag}&_include=DiagnosticReport:Patient:patient&_include=DiagnosticReport:Observation:result&code=429858000&_lastUpdated=lt{Fixture.PatientGroup.Meta.LastUpdated.Value:o}";

            Bundle bundle = await Client.SearchAsync(ResourceType.DiagnosticReport, query);

            ValidateBundle(
                bundle,
                Fixture.SmithSnomedDiagnosticReport,
                Fixture.SmithPatient,
                Fixture.SmithSnomedObservation,
                Fixture.TrumanSnomedDiagnosticReport,
                Fixture.TrumanPatient,
                Fixture.TrumanSnomedObservation);
        }
    }
}
