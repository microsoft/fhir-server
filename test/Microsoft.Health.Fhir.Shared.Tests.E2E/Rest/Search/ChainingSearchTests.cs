// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
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
        public async Task GivenANestedReverseChainSearchExpressionOverADenormalizedParameter_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&_has:Group:member:_id={Fixture.PatientGroup.Id}";

            Bundle bundle = await Client.SearchAsync(ResourceType.Patient, query);

            ValidateBundle(bundle, Fixture.AdamsPatient, Fixture.SmithPatient, Fixture.TrumanPatient);
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

                var organization = FhirClient.CreateAsync(new Organization { Meta = meta, Address = new List<Address> { new Address { City = "Seattle" } } }).Result.Resource;

                AdamsPatient = FhirClient.CreateAsync(new Patient { Meta = meta, Name = new List<HumanName> { new HumanName { Family = "Adams" } } }).Result.Resource;
                SmithPatient = FhirClient.CreateAsync(new Patient { Meta = meta, Name = new List<HumanName> { new HumanName { Family = "Smith" } }, ManagingOrganization = new ResourceReference($"Organization/{organization.Id}") }).Result.Resource;
                TrumanPatient = FhirClient.CreateAsync(new Patient { Meta = meta, Name = new List<HumanName> { new HumanName { Family = "Truman" } } }).Result.Resource;

                var adamsLoincObservation = CreateObservation(AdamsPatient, loincCode);
                var smithLoincObservation = CreateObservation(SmithPatient, loincCode);
                var smithSnomedObservation = CreateObservation(SmithPatient, snomedCode);
                var trumanLoincObservation = CreateObservation(TrumanPatient, loincCode);
                var trumanSnomedObservation = CreateObservation(TrumanPatient, snomedCode);

                SmithSnomedDiagnosticReport = CreateDiagnosticReport(SmithPatient, smithSnomedObservation, snomedCode);
                TrumanSnomedDiagnosticReport = CreateDiagnosticReport(TrumanPatient, trumanSnomedObservation, snomedCode);
                SmithLoincDiagnosticReport = CreateDiagnosticReport(SmithPatient, smithLoincObservation, loincCode);
                TrumanLoincDiagnosticReport = CreateDiagnosticReport(TrumanPatient, trumanLoincObservation, loincCode);

                var group = new Group
                {
                    Meta = new Meta { Tag = new List<Coding> { new Coding("testTag", Tag) } },
                    Type = Group.GroupType.Person, Actual = true,
                    Member = new List<Group.MemberComponent>
                    {
                        new Group.MemberComponent { Entity = new ResourceReference($"Patient/{AdamsPatient.Id}") },
                        new Group.MemberComponent { Entity = new ResourceReference($"Patient/{SmithPatient.Id}") },
                        new Group.MemberComponent { Entity = new ResourceReference($"Patient/{TrumanPatient.Id}") },
                    },
                };

                PatientGroup = FhirClient.CreateAsync(group).Result.Resource;

                DiagnosticReport CreateDiagnosticReport(Patient patient, Observation observation, CodeableConcept code)
                {
                    return FhirClient.CreateAsync(
                        new DiagnosticReport
                        {
                            Meta = meta,
                            Status = DiagnosticReport.DiagnosticReportStatus.Final,
                            Code = code,
                            Subject = new ResourceReference($"Patient/{patient.Id}"),
                            Result = new List<ResourceReference> { new ResourceReference($"Observation/{observation.Id}") },
                        }).Result.Resource;
                }

                Observation CreateObservation(Patient patient, CodeableConcept code)
                {
                    return FhirClient.CreateAsync(
                        new Observation()
                        {
                            Status = ObservationStatus.Final,
                            Code = code,
                            Subject = new ResourceReference($"Patient/{patient.Id}"),
                        }).Result.Resource;
                }
            }

            public Group PatientGroup { get; }

            public string Tag { get; }

            public Patient AdamsPatient { get; }

            public Patient TrumanPatient { get; }

            public DiagnosticReport TrumanSnomedDiagnosticReport { get; }

            public DiagnosticReport TrumanLoincDiagnosticReport { get; }

            public Patient SmithPatient { get; }

            public DiagnosticReport SmithSnomedDiagnosticReport { get; }

            public DiagnosticReport SmithLoincDiagnosticReport { get; }
        }
    }
}
