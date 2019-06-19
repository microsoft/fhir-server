// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Rest;
using Microsoft.Health.Fhir.Tests.E2E.Rest.Search;
using Microsoft.Health.Fhir.Web;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Shared.Tests.E2E.Rest.Search
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

            ValidateBundle(bundle, Fixture.SmithDiagnosticReport);
        }

        [Fact]
        public async Task GivenANestedChainedSearchExpression_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&result.subject:Patient.name=Smith";

            Bundle bundle = await Client.SearchAsync(ResourceType.DiagnosticReport, query);

            ValidateBundle(bundle, Fixture.SmithDiagnosticReport);
        }

        [Fact]
        public async Task GivenANestedChainedSearchExpressionWithAnOrFinalCondition_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&result.subject:Patient.name=Smith,Truman";

            Bundle bundle = await Client.SearchAsync(ResourceType.DiagnosticReport, query);

            ValidateBundle(bundle, Fixture.SmithDiagnosticReport, Fixture.TrumanDiagnosticReport);
        }

        [Fact]
        public async Task GivenAChainedSearchExpressionOverASimpleParameter_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&subject:Patient._type=Patient";

            Bundle bundle = await Client.SearchAsync(ResourceType.DiagnosticReport, query);

            ValidateBundle(bundle, Fixture.SmithDiagnosticReport, Fixture.TrumanDiagnosticReport);
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
        public async Task GivenANestedReverseChainSearchExpressionOverASimpleParameter_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&_has:Observation:patient:_has:DiagnosticReport:result:code=429858000";

            Bundle bundle = await Client.SearchAsync(ResourceType.Patient, query);

            ValidateBundle(bundle, Fixture.SmithPatient, Fixture.TrumanPatient);
        }

        [Fact]
        public async Task GivenACombinationOfChainingReverseChainSearchExpressionOverASimpleParameter_WhenSearched_ThenCorrectBundleShouldBeReturned()
        {
            string query = $"_tag={Fixture.Tag}&patient:Patient._has:Observation:subject:code=429858000";

            Bundle bundle = await Client.SearchAsync(ResourceType.DiagnosticReport, query);

            ValidateBundle(bundle, Fixture.SmithDiagnosticReport, Fixture.TrumanDiagnosticReport);
        }

        public class ClassFixture : HttpIntegrationTestFixture<Startup>
        {
            public ClassFixture(DataStore dataStore, Format format)
                : base(dataStore, format)
            {
                Tag = Guid.NewGuid().ToString();

                // Construct an observation pointing to a patient and a diagnostic report pointing to the observation and the patient

                SmithPatient = FhirClient.CreateAsync(new Patient { Meta = new Meta { Tag = new List<Coding> { new Coding("testTag", Tag) } }, Name = new List<HumanName> { new HumanName { Family = "Smith" } } }).Result.Resource;
                TrumanPatient = FhirClient.CreateAsync(new Patient { Meta = new Meta { Tag = new List<Coding> { new Coding("testTag", Tag) } }, Name = new List<HumanName> { new HumanName { Family = "Truman" } } }).Result.Resource;

                var smithObservation = CreateObservation(SmithPatient);
                var trumanObservation = CreateObservation(TrumanPatient);

                SmithDiagnosticReport = CreateDiagnosticReport(SmithPatient, smithObservation);
                TrumanDiagnosticReport = CreateDiagnosticReport(TrumanPatient, trumanObservation);

                DiagnosticReport CreateDiagnosticReport(Patient patient, Observation observation)
                {
                    return FhirClient.CreateAsync(
                        new DiagnosticReport
                        {
                            Meta = new Meta { Tag = new List<Coding> { new Coding("testTag", Tag) } },
                            Status = DiagnosticReport.DiagnosticReportStatus.Final,
                            Code = new CodeableConcept("http://snomed.info/sct", "429858000"),
                            Subject = new ResourceReference($"Patient/{patient.Id}"),
                            Result = new List<ResourceReference> { new ResourceReference($"Observation/{observation.Id}") },
                        }).Result.Resource;
                }

                Observation CreateObservation(Patient patient)
                {
                    return FhirClient.CreateAsync(
                        new Observation()
                        {
                            Status = ObservationStatus.Final,
                            Code = new CodeableConcept("http://snomed.info/sct", "429858000"),
                            Subject = new ResourceReference($"Patient/{patient.Id}"),
                        }).Result.Resource;
                }
            }

            public string Tag { get; }

            public Patient SmithPatient { get; }

            public Patient TrumanPatient { get; }

            public DiagnosticReport TrumanDiagnosticReport { get; }

            public DiagnosticReport SmithDiagnosticReport { get; }
        }
    }
}
