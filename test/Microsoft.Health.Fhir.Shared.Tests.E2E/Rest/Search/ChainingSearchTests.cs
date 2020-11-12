// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Search
{
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
    public class ChainingSearchTests : SearchTestsBase<ChainingSearchTests.ClassFixture>
    {
        public ChainingSearchTests(ClassFixture fixture)
            : base(fixture)
        {
        }

        [Fact]
        public async Task GivenAChainedSearchExpression_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&subject:Patient.name=Smith";

            Bundle bundle = await Client.SearchAsync(ResourceType.DiagnosticReport, query);

            ValidateBundle(bundle, Fixture.SmithSnomedDiagnosticReport, Fixture.SmithLoincDiagnosticReport);
        }

        [Fact]
        public async Task GivenANestedChainedSearchExpression_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&result.subject:Patient.name=Smith";

            Bundle bundle = await Client.SearchAsync(ResourceType.DiagnosticReport, query);

            ValidateBundle(bundle, Fixture.SmithSnomedDiagnosticReport, Fixture.SmithLoincDiagnosticReport);
        }

        [Fact]
        public async Task GivenAMultiNestedChainedSearchExpression_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&result.subject:Patient.organization.address-city=Seattle";

            Bundle bundle = await Client.SearchAsync(ResourceType.DiagnosticReport, query);

            ValidateBundle(bundle, Fixture.SmithSnomedDiagnosticReport, Fixture.SmithLoincDiagnosticReport);
        }

        [Fact]
        public async Task GivenANestedChainedSearchExpressionWithAnOrFinalCondition_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&result.subject:Patient.name=Smith,Truman";

            Bundle bundle = await Client.SearchAsync(ResourceType.DiagnosticReport, query);

            ValidateBundle(bundle, Fixture.SmithSnomedDiagnosticReport, Fixture.SmithLoincDiagnosticReport, Fixture.TrumanSnomedDiagnosticReport, Fixture.TrumanLoincDiagnosticReport);
        }

        [Fact]
        public async Task GivenAChainedSearchExpressionOverASimpleParameter_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&subject:Patient._type=Patient";

            Bundle bundle = await Client.SearchAsync(ResourceType.DiagnosticReport, query);

            ValidateBundle(bundle, Fixture.SmithSnomedDiagnosticReport, Fixture.SmithLoincDiagnosticReport, Fixture.TrumanSnomedDiagnosticReport, Fixture.TrumanLoincDiagnosticReport);
        }

        [Fact]
        public async Task GivenAChainedSearchExpressionOverASimpleParameter_WhenSearchedWithPaging_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&subject:Patient._type=Patient&_count=2";

            Bundle bundle = await Client.SearchAsync(ResourceType.DiagnosticReport, query);

            ValidateBundle(bundle, Fixture.SmithSnomedDiagnosticReport, Fixture.TrumanSnomedDiagnosticReport);

            bundle = await Client.SearchAsync(bundle.NextLink.ToString());

            ValidateBundle(bundle, Fixture.SmithLoincDiagnosticReport, Fixture.TrumanLoincDiagnosticReport);
        }

        [Fact]
        public async Task GivenAChainedSearchExpressionOverASimpleParameterWithNoResults_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&subject:Patient._type=Observation";

            Bundle bundle = await Client.SearchAsync(ResourceType.DiagnosticReport, query);

            ValidateBundle(bundle);
        }

        [Fact]
        public async Task GivenAReverseChainSearchExpressionOverASimpleParameter_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&_has:Observation:patient:code=429858000";

            Bundle bundle = await Client.SearchAsync(ResourceType.Patient, query);

            ValidateBundle(bundle, Fixture.SmithPatient, Fixture.TrumanPatient);
        }

        [Fact]
        public async Task GivenAReverseChainSearchExpressionOverASimpleParameter_WhenSearchedWithPaging_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&_has:Observation:patient:code=429858000&_count=1";

            Bundle bundle = await Client.SearchAsync(ResourceType.Patient, query);

            ValidateBundle(bundle, Fixture.SmithPatient);

            bundle = await Client.SearchAsync(bundle.NextLink.ToString());

            ValidateBundle(bundle, Fixture.TrumanPatient);
        }

        [Fact]
        public async Task GivenANestedReverseChainSearchExpressionOverASimpleParameter_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&_has:Observation:patient:_has:DiagnosticReport:result:code=429858000";

            Bundle bundle = await Client.SearchAsync(ResourceType.Patient, query);

            ValidateBundle(bundle, Fixture.SmithPatient, Fixture.TrumanPatient);
        }

        [Fact]
        public async Task GivenANestedReverseChainSearchExpressionOverTheIdDenormalizedParameter_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&_has:Group:member:_id={Fixture.PatientGroup.Id}";

            Bundle bundle = await Client.SearchAsync(ResourceType.Patient, query);

            ValidateBundle(bundle, Fixture.AdamsPatient, Fixture.SmithPatient, Fixture.TrumanPatient);
        }

        [Fact]
        public async Task GivenANestedReverseChainSearchExpressionOverTheTypeDenormalizedParameter_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            Bundle bundle = await Client.SearchAsync(ResourceType.Patient, $"_tag={Fixture.Tag}&_has:Group:member:_type=Group");

            Assert.NotEmpty(bundle.Entry);

            bundle = await Client.SearchAsync(ResourceType.Patient, $"_tag={Fixture.Tag}&_has:Group:member:_type=Patient");

            Assert.Empty(bundle.Entry);
        }

        [Fact]
        public async Task GivenAChainedSearchExpressionWithAPredicateOnSurrogateId_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"subject:Patient._type=Patient&subject:Patient._tag={Fixture.Tag}";

            Bundle completeBundle = await Client.SearchAsync(ResourceType.DiagnosticReport, query);
            Assert.True(completeBundle.Entry.Count > 2);

            Bundle bundle = await Client.SearchAsync(ResourceType.DiagnosticReport, query, count: 1);
            List<Bundle.EntryComponent> resources = new List<Bundle.EntryComponent>();
            resources.AddRange(bundle.Entry);
            while (bundle.NextLink != null)
            {
                bundle = await Client.SearchAsync(bundle.NextLink.ToString());
                resources.AddRange(bundle.Entry);
            }

            ValidateBundle(new Bundle { Entry = resources }, completeBundle.Entry.Select(e => e.Resource).ToArray());
        }

        [Fact]
        public async Task GivenAReverseChainedSearchExpressionWithAPredicateOnSurrogateId_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_has:Observation:patient:_tag={Fixture.Tag}";

            Bundle completeBundle = await Client.SearchAsync(ResourceType.Patient, query);
            Assert.True(completeBundle.Entry.Count > 2);

            Bundle bundle = await Client.SearchAsync(ResourceType.Patient, query, count: 1);
            List<Bundle.EntryComponent> resources = new List<Bundle.EntryComponent>();
            resources.AddRange(bundle.Entry);
            while (bundle.NextLink != null)
            {
                bundle = await Client.SearchAsync(bundle.NextLink.ToString());
                resources.AddRange(bundle.Entry);
            }

            ValidateBundle(new Bundle { Entry = resources }, completeBundle.Entry.Select(e => e.Resource).ToArray());
        }

        [Fact]
        public async Task GivenACombinationOfChainingReverseChainSearchExpressionOverASimpleParameter_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&code=429858000&patient:Patient._has:Group:member:_tag={Fixture.Tag}";

            Bundle bundle = await Client.SearchAsync(ResourceType.DiagnosticReport, query);

            ValidateBundle(bundle, Fixture.SmithSnomedDiagnosticReport, Fixture.TrumanSnomedDiagnosticReport);
        }

        [Fact]
        public async Task GivenACombinationOfChainingReverseChainSearchExpressionOverASimpleParameter_WhenSearchedWithPaging_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&code=429858000&patient:Patient._has:Group:member:_tag={Fixture.Tag}&_count=1";

            Bundle bundle = await Client.SearchAsync(ResourceType.DiagnosticReport, query);

            ValidateBundle(bundle, Fixture.SmithSnomedDiagnosticReport);

            bundle = await Client.SearchAsync(bundle.NextLink.ToString());

            ValidateBundle(bundle, Fixture.TrumanSnomedDiagnosticReport);
        }

        [Fact]
        public async Task GivenACombinationOfChainingReverseChainSearchExpressionOverADenormalizedParameter_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&code=429858000&patient:Patient._has:Group:member:_id={Fixture.PatientGroup.Id}";

            Bundle bundle = await Client.SearchAsync(ResourceType.DiagnosticReport, query);

            ValidateBundle(bundle, Fixture.SmithSnomedDiagnosticReport, Fixture.TrumanSnomedDiagnosticReport);
        }

        [Fact]
        public async Task GivenACombinationOfChainingReverseChainSearchExpressionOverADenormalizedParameter_WhenSearchedWithPaging_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&code=429858000&patient:Patient._has:Group:member:_id={Fixture.PatientGroup.Id}&_count=1";

            Bundle bundle = await Client.SearchAsync(ResourceType.DiagnosticReport, query);

            ValidateBundle(bundle, Fixture.SmithSnomedDiagnosticReport);

            bundle = await Client.SearchAsync(bundle.NextLink.ToString());

            ValidateBundle(bundle, Fixture.TrumanSnomedDiagnosticReport);
        }

        public class ClassFixture : HttpIntegrationTestFixture
        {
            public ClassFixture(DataStore dataStore, Format format, TestFhirServerFactory testFhirServerFactory)
                : base(dataStore, format, testFhirServerFactory)
            {
            }

            public Group PatientGroup { get; private set; }

            public string Tag { get; private set; }

            public Patient AdamsPatient { get; private set; }

            public Patient TrumanPatient { get; private set; }

            public DiagnosticReport TrumanSnomedDiagnosticReport { get; private set; }

            public DiagnosticReport TrumanLoincDiagnosticReport { get; private set; }

            public Patient SmithPatient { get; private set; }

            public DiagnosticReport SmithSnomedDiagnosticReport { get; private set; }

            public DiagnosticReport SmithLoincDiagnosticReport { get; private set; }

            protected override async Task OnInitializedAsync()
            {
                Tag = Guid.NewGuid().ToString();

                // Construct an observation pointing to a patient and a diagnostic report pointing to the observation and the patient along with some not matching entries
                var snomedCode = new CodeableConcept("http://snomed.info/sct", "429858000");
                var loincCode = new CodeableConcept("http://loinc.org", "4548-4");

                var meta = new Meta
                {
                    Tag = new List<Coding>
                    {
                        new Coding("testTag", Tag),
                    },
                };

                var organization = (await TestFhirClient.CreateAsync(new Organization { Meta = meta, Address = new List<Address> { new Address { City = "Seattle" } } })).Resource;

                AdamsPatient = (await TestFhirClient.CreateAsync(new Patient { Meta = meta, Name = new List<HumanName> { new HumanName { Family = "Adams" } } })).Resource;
                SmithPatient = (await TestFhirClient.CreateAsync(new Patient { Meta = meta, Name = new List<HumanName> { new HumanName { Family = "Smith" } }, ManagingOrganization = new ResourceReference($"Organization/{organization.Id}") })).Resource;
                TrumanPatient = (await TestFhirClient.CreateAsync(new Patient { Meta = meta, Name = new List<HumanName> { new HumanName { Family = "Truman" } } })).Resource;

                var adamsLoincObservation = await CreateObservation(AdamsPatient, loincCode);
                var smithLoincObservation = await CreateObservation(SmithPatient, loincCode);
                var smithSnomedObservation = await CreateObservation(SmithPatient, snomedCode);
                var trumanLoincObservation = await CreateObservation(TrumanPatient, loincCode);
                var trumanSnomedObservation = await CreateObservation(TrumanPatient, snomedCode);

                SmithSnomedDiagnosticReport = await CreateDiagnosticReport(SmithPatient, smithSnomedObservation, snomedCode);
                TrumanSnomedDiagnosticReport = await CreateDiagnosticReport(TrumanPatient, trumanSnomedObservation, snomedCode);
                SmithLoincDiagnosticReport = await CreateDiagnosticReport(SmithPatient, smithLoincObservation, loincCode);
                TrumanLoincDiagnosticReport = await CreateDiagnosticReport(TrumanPatient, trumanLoincObservation, loincCode);

                var group = new Group
                {
                    Meta = new Meta { Tag = new List<Coding> { new Coding("testTag", Tag) } },
                    Type = Group.GroupType.Person,
                    Actual = true,
                    Member = new List<Group.MemberComponent>
                    {
                        new Group.MemberComponent { Entity = new ResourceReference($"Patient/{AdamsPatient.Id}") },
                        new Group.MemberComponent { Entity = new ResourceReference($"Patient/{SmithPatient.Id}") },
                        new Group.MemberComponent { Entity = new ResourceReference($"Patient/{TrumanPatient.Id}") },
                    },
                };

                PatientGroup = (await TestFhirClient.CreateAsync(group)).Resource;

                async Task<DiagnosticReport> CreateDiagnosticReport(Patient patient, Observation observation, CodeableConcept code)
                {
                    return (await TestFhirClient.CreateAsync(
                        new DiagnosticReport
                        {
                            Meta = meta,
                            Status = DiagnosticReport.DiagnosticReportStatus.Final,
                            Code = code,
                            Subject = new ResourceReference($"Patient/{patient.Id}"),
                            Result = new List<ResourceReference> { new ResourceReference($"Observation/{observation.Id}") },
                        })).Resource;
                }

                async Task<Observation> CreateObservation(Patient patient, CodeableConcept code)
                {
                    return (await TestFhirClient.CreateAsync(
                        new Observation()
                        {
                            Meta = meta,
                            Status = ObservationStatus.Final,
                            Code = code,
                            Subject = new ResourceReference($"Patient/{patient.Id}"),
                        })).Resource;
                }
            }
        }
    }
}
