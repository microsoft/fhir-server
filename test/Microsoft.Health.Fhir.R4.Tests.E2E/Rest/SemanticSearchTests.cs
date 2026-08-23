// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Extensions.Xunit;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch;
using Microsoft.Health.Fhir.Core.Models;
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
            await EnsureCoverageSearchParameterIsEnabledAsync();

            Patient patient = await CreateAsync(new Patient { Active = true });
            Patient otherPatient = await CreateAsync(new Patient { Active = true });
            Organization payor = await CreateAsync(new Organization { Active = true, Name = "Semantic search test payor" });
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
            Coverage coverage = await CreateAsync(CreateCoverage(patient.Id, payor.Id, Query));
            Observation otherPatientObservation = await CreateAsync(new Observation
            {
                Status = ObservationStatus.Final,
                Code = new CodeableConcept("http://loinc.org", "75325-1", "Symptom"),
                Subject = new ResourceReference($"Patient/{otherPatient.Id}"),
                Note = { new Annotation { Text = Query } },
            });
            Coverage otherPatientCoverage = await CreateAsync(CreateCoverage(otherPatient.Id, payor.Id, Query));
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
            Assert.Equal(4, bundle.Total);
            Assert.Equal(
                new[] { documentReference.Id, observation.Id, diagnosticReport.Id, coverage.Id }.OrderBy(id => id),
                bundle.Entry.Select(entry => entry.Resource.Id).OrderBy(id => id));
            Assert.DoesNotContain(bundle.Entry, entry => entry.Resource.Id == otherPatientObservation.Id);
            Assert.DoesNotContain(bundle.Entry, entry => entry.Resource.Id == otherPatientCoverage.Id);
            Assert.Equal(
                bundle.Entry.Select(entry => entry.Search.Score).OrderByDescending(score => score),
                bundle.Entry.Select(entry => entry.Search.Score));

            Bundle.EntryComponent observationEntry = Assert.Single(bundle.Entry, entry => entry.Resource.Id == observation.Id);
            Assert.Equal(1m, observationEntry.Search.Score);
            Bundle.EntryComponent coverageEntry = Assert.Single(bundle.Entry, entry => entry.Resource.Id == coverage.Id);
            Assert.Equal(1m, coverageEntry.Search.Score);

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

            var coverageOnlyParameters = new Parameters
            {
                Parameter =
                {
                    new Parameters.ParameterComponent { Name = "query", Value = new FhirString(Query) },
                    new Parameters.ParameterComponent { Name = "count", Value = new Integer(10) },
                    new Parameters.ParameterComponent { Name = "type", Value = new Code(ResourceType.Coverage.ToString()) },
                },
            };

            using FhirResponse<Resource> coverageOnlyResponse = await _client.PostAsync(
                $"Patient/{patient.Id}/$semantic-search",
                coverageOnlyParameters.ToJson());

            Bundle coverageOnlyBundle = Assert.IsType<Bundle>(coverageOnlyResponse.Resource);
            Bundle.EntryComponent onlyCoverageEntry = Assert.Single(coverageOnlyBundle.Entry);
            Assert.Equal(coverage.Id, onlyCoverageEntry.Resource.Id);
        }

        [Fact]
        public async Task GivenDocumentReferenceBeforeBinaryInTransaction_WhenSemanticSearchIsInvoked_ThenDocumentReferenceContainsBinaryEvidence()
        {
            // Arrange
            string suffix = Guid.NewGuid().ToString("N");
            string documentReferenceId = $"semantic-document-{suffix}";
            string binaryId = $"semantic-binary-{suffix}";
            string passage = $"{Query} transaction {suffix}";
            var transaction = new Bundle
            {
                Type = Bundle.BundleType.Transaction,
                Entry =
                {
                    new Bundle.EntryComponent
                    {
                        Resource = new DocumentReference
                        {
                            Id = documentReferenceId,
                            Status = DocumentReferenceStatus.Current,
                            Content =
                            {
                                new DocumentReference.ContentComponent
                                {
                                    Attachment = new Attachment
                                    {
                                        ContentType = "text/plain",
                                        Url = $"Binary/{binaryId}",
                                    },
                                },
                            },
                        },
                        Request = new Bundle.RequestComponent
                        {
                            Method = Bundle.HTTPVerb.PUT,
                            Url = $"DocumentReference/{documentReferenceId}",
                        },
                    },
                    new Bundle.EntryComponent
                    {
                        Resource = new Binary
                        {
                            Id = binaryId,
                            ContentType = "text/plain",
                            Data = Encoding.UTF8.GetBytes(passage),
                        },
                        Request = new Bundle.RequestComponent
                        {
                            Method = Bundle.HTTPVerb.PUT,
                            Url = $"Binary/{binaryId}",
                        },
                    },
                },
            };

            // Act
            using FhirResponse<Bundle> transactionResponse = await _client.PostBundleAsync(
                transaction,
                new FhirBundleOptions { BundleProcessingLogic = FhirBundleProcessingLogic.Sequential });
            Bundle searchResult = await _client.SearchAsync(
                ResourceType.DocumentReference,
                $"semantic-text={Uri.EscapeDataString(passage)}&_count=1");

            // Assert
            Assert.All(transactionResponse.Resource.Entry, entry => Assert.Equal("201", entry.Response.Status));
            Bundle.EntryComponent resultEntry = Assert.Single(
                searchResult.Entry,
                entry => entry.Search.Mode == Bundle.SearchEntryMode.Match && entry.Resource.Id == documentReferenceId);
            string binaryReference = $"Binary/{binaryId}/_history/1";
            Assert.Contains(
                resultEntry.Search.Extension,
                extension =>
                    extension.Url == SemanticSearchEvidence.ExtensionUrl &&
                    extension.Extension.Any(component =>
                        component.Url == SemanticSearchEvidence.SourceExtensionUrl &&
                        component.Value is ResourceReference source &&
                        source.Reference == binaryReference) &&
                    extension.Extension.Any(component =>
                        component.Url == SemanticSearchEvidence.TextExtensionUrl &&
                        component.Value is FhirString text &&
                        text.Value == passage));
        }

        private async Task EnsureCoverageSearchParameterIsEnabledAsync()
        {
            var searchParameter = new SearchParameter
            {
                Id = "coverage-semantic",
                Url = SemanticSearchTestParameterResolver.CoverageCanonical.ToString(),
                Name = "CoverageSemantic",
                Status = PublicationStatus.Active,
                Code = "semantic",
                Type = SearchParamType.Special,
                Expression = "Coverage.class.name",
                Description = new Markdown("Semantic text in the Coverage plan name."),
                Base = new ResourceType?[] { ResourceType.Coverage },
                Extension =
                {
                    new Extension
                    {
                        Url = VectorSearchParameterConfig.ExtensionUrl,
                        Extension =
                        {
                            new Extension(VectorSearchParameterConfig.SourceStrategyExtensionUrl, new Code("directText")),
                            new Extension(VectorSearchParameterConfig.ExtractionPolicyExtensionUrl, new Code("perValueRow")),
                        },
                    },
                },
            };

            var updateStopwatch = Stopwatch.StartNew();
            while (true)
            {
                try
                {
                    using FhirResponse<SearchParameter> updateResponse = await _client.UpdateAsync(searchParameter);
                    break;
                }
                catch (FhirClientException exception) when (
                    exception.StatusCode == HttpStatusCode.Conflict &&
                    updateStopwatch.Elapsed < TimeSpan.FromMinutes(2))
                {
                    exception.Dispose();
                    await Task.Delay(TimeSpan.FromSeconds(1));
                }
            }

            (_, Uri jobUri) = await _client.PostReindexJobAsync(new Parameters());
            var stopwatch = Stopwatch.StartNew();

            while (stopwatch.Elapsed < TimeSpan.FromMinutes(2))
            {
                using FhirResponse<Parameters> jobResponse = await _client.CheckJobAsync(jobUri);
                DataType statusValue = jobResponse.Resource?.Parameter?.FirstOrDefault(parameter => parameter.Name == "status")?.Value;
                string status = statusValue switch
                {
                    Code code => code.Value,
                    FhirString text => text.Value,
                    _ => null,
                };

                if (string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                Assert.False(
                    string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(status, "Canceled", StringComparison.OrdinalIgnoreCase),
                    $"Coverage SearchParameter reindex ended with status '{status}'.");
                await Task.Delay(TimeSpan.FromSeconds(1));
            }

            Assert.Fail("Coverage SearchParameter reindex did not complete within two minutes.");
        }

        private static Coverage CreateCoverage(string patientId, string payorId, string text)
        {
            return new Coverage
            {
                Status = FinancialResourceStatusCodes.Active,
                Beneficiary = new ResourceReference($"Patient/{patientId}"),
                Payor = { new ResourceReference($"Organization/{payorId}") },
                Class =
                {
                    new Coverage.ClassComponent
                    {
                        Type = new CodeableConcept("http://terminology.hl7.org/CodeSystem/coverage-class", "plan"),
                        Value = "semantic-plan",
                        Name = text,
                    },
                },
            };
        }

        private async Task<T> CreateAsync<T>(T resource)
            where T : Resource
        {
            using FhirResponse<T> response = await _client.CreateAsync(resource);
            return response.Resource;
        }
    }
}
