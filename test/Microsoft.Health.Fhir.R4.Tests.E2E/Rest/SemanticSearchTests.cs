// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Extensions.Xunit;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
    public sealed class SemanticSearchTests : IClassFixture<SemanticSearchTestFixture>
    {
        private const string Query = "difficulty breathing after exercise";
        private readonly TestFhirClient _client;

        public SemanticSearchTests(SemanticSearchTestFixture fixture)
        {
            _client = fixture.TestFhirClient;
        }

        [Fact]
        public async Task GivenPatientResources_WhenSemanticSearchIsInvoked_ThenMixedRankedBundleContainsEvidence()
        {
            Patient patient = await CreateAsync(new Patient { Active = true });
            Patient otherPatient = await CreateAsync(new Patient { Active = true });
            Binary binary = await CreateAsync(new Binary
            {
                ContentType = "text/plain",
                Data = Encoding.UTF8.GetBytes("The patient reports shortness of breath while climbing stairs."),
            });
            DocumentReference documentReference = await CreateAsync(new DocumentReference
            {
                Status = DocumentReferenceStatus.Current,
                Subject = new ResourceReference($"Patient/{patient.Id}"),
                Content =
                {
                    new DocumentReference.ContentComponent
                    {
                        Attachment = new Attachment
                        {
                            ContentType = "text/plain",
                            Url = $"Binary/{binary.Id}",
                        },
                    },
                },
            });
            Observation observation = await CreateAsync(new Observation
            {
                Status = ObservationStatus.Final,
                Code = new CodeableConcept("http://loinc.org", "75325-1", "Symptom"),
                Subject = new ResourceReference($"Patient/{patient.Id}"),
                Note = { new Annotation { Text = Query } },
            });
            DiagnosticReport diagnosticReport = await CreateAsync(new DiagnosticReport
            {
                Status = DiagnosticReport.DiagnosticReportStatus.Final,
                Code = new CodeableConcept("http://loinc.org", "19868-9", "Pulmonary function study"),
                Subject = new ResourceReference($"Patient/{patient.Id}"),
                Conclusion = "Pulmonary testing indicates exertional airflow limitation.",
            });
            Observation otherPatientObservation = await CreateAsync(new Observation
            {
                Status = ObservationStatus.Final,
                Code = new CodeableConcept("http://loinc.org", "75325-1", "Symptom"),
                Subject = new ResourceReference($"Patient/{otherPatient.Id}"),
                Note = { new Annotation { Text = Query } },
            });
            var parameters = new Parameters
            {
                Parameter =
                {
                    new Parameters.ParameterComponent { Name = "query", Value = new FhirString(Query) },
                    new Parameters.ParameterComponent { Name = "count", Value = new Integer(10) },
                },
            };

            using FhirResponse<Resource> response = await _client.PostAsync(
                $"Patient/{patient.Id}/$semantic-search",
                parameters.ToJson());

            Bundle bundle = Assert.IsType<Bundle>(response.Resource);
            Assert.Equal(3, bundle.Total);
            Assert.Equal(
                new[] { documentReference.Id, observation.Id, diagnosticReport.Id }.OrderBy(id => id),
                bundle.Entry.Select(entry => entry.Resource.Id).OrderBy(id => id));
            Assert.DoesNotContain(bundle.Entry, entry => entry.Resource.Id == otherPatientObservation.Id);
            Assert.Equal(
                bundle.Entry.Select(entry => entry.Search.Score).OrderByDescending(score => score),
                bundle.Entry.Select(entry => entry.Search.Score));

            Bundle.EntryComponent observationEntry = Assert.Single(bundle.Entry, entry => entry.Resource.Id == observation.Id);
            Assert.Equal(1m, observationEntry.Search.Score);

            Bundle.EntryComponent documentReferenceEntry = Assert.Single(bundle.Entry, entry => entry.Resource.Id == documentReference.Id);
            Extension documentEvidence = Assert.Single(
                documentReferenceEntry.Search.Extension,
                extension => extension.Url == SemanticSearchEvidence.ExtensionUrl);
            Assert.Equal(
                $"Binary/{binary.Id}/_history/{binary.Meta.VersionId}",
                ((ResourceReference)documentEvidence.Extension.Single(extension => extension.Url == SemanticSearchEvidence.SourceExtensionUrl).Value).Reference);
            Assert.Equal(
                "Binary.data",
                ((FhirString)documentEvidence.Extension.Single(extension => extension.Url == SemanticSearchEvidence.SourcePathExtensionUrl).Value).Value);
            Assert.Equal(
                "The patient reports shortness of breath while climbing stairs.",
                ((FhirString)documentEvidence.Extension.Single(extension => extension.Url == SemanticSearchEvidence.TextExtensionUrl).Value).Value);
        }

        private async Task<T> CreateAsync<T>(T resource)
            where T : Resource
        {
            using FhirResponse<T> response = await _client.CreateAsync(resource);
            return response.Resource;
        }
    }
}
