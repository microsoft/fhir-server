// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Conformance
{
    public class DocRefOperationTestFixture : HttpIntegrationTestFixture
    {
        private const int PatientCount = 3;
        private const int DocumentReferenceCount = 12;
        private const string DocumentSystem = "http://loinc.org";

        // https://hl7.org/fhir/R4/valueset-c80-doc-typecodes.html
        private static readonly string[] DocumentTypes =
        {
            "55107-7",
            "74155-3",
            "51851-4",
            "67851-6",
            "34744-3",
            "34873-0",
        };

        private List<Patient> _patients;
        private Dictionary<string, List<DocumentReference>> _documentReferences;
        private readonly DateTime _baseTime;
        private readonly string _tag;

        public DocRefOperationTestFixture(
            DataStore dataStore,
            Format format,
            TestFhirServerFactory testFhirServerFactory)
            : base(dataStore, format, testFhirServerFactory)
        {
            _patients = new List<Patient>();
            _documentReferences = new Dictionary<string, List<DocumentReference>>(StringComparer.OrdinalIgnoreCase);
            _baseTime = DateTime.UtcNow;
            _tag = $"docreftest-{_baseTime.Ticks}";
        }

        public DateTime BaseTime => _baseTime;

        public IReadOnlyList<Patient> Patients => _patients;

        public IReadOnlyList<DocumentReference> GetDocumentReferences(string patientId)
        {
            if (_documentReferences.TryGetValue(patientId, out var documentReferences))
            {
                return documentReferences;
            }

            return new List<DocumentReference>();
        }

        protected override async Task OnInitializedAsync()
        {
            await base.OnInitializedAsync();

            var bundle = new Bundle()
            {
                Type = Bundle.BundleType.Batch,
            };

            for (int i = 0; i < PatientCount; i++)
            {
                var patient = new Patient()
                {
                    Id = Guid.NewGuid().ToString(),
                    Meta = new Meta()
                    {
                        Tag = new List<Coding>
                        {
                            new Coding()
                            {
                                Code = _tag,
                                System = nameof(DocRefOperationTests).ToLowerInvariant(),
                            },
                        },
                    },
                };

                var entry = new Bundle.EntryComponent()
                {
                    Resource = patient,
                    Request = new Bundle.RequestComponent()
                    {
                        Method = Bundle.HTTPVerb.POST,
                        Url = KnownResourceTypes.Patient,
                    },
                };

                bundle.Entry.Add(entry);
            }

            using var createPatientsResponse = await TestFhirClient.PostBundleAsync(bundle);
            Assert.Equal(HttpStatusCode.OK, createPatientsResponse.StatusCode);

            _patients = createPatientsResponse.Resource.Entry
                .Select(x => (Patient)x.Resource)
                .ToList();
            bundle.Entry.Clear();

            foreach (var patient in _patients)
            {
                _documentReferences.Add(patient.Id, new List<DocumentReference>());
                for (int j = 0; j < DocumentReferenceCount; j++)
                {
                    var id = Guid.NewGuid().ToString();
                    var documentReference = new DocumentReference()
                    {
                        Id = id,
                        Meta = new Meta()
                        {
                            Tag = new List<Coding>
                            {
                                new Coding()
                                {
                                    Code = _tag,
                                    System = nameof(DocRefOperationTests).ToLowerInvariant(),
                                },
                            },
                        },
                        Subject = new ResourceReference(patient.Id),
                        Type = new CodeableConcept("http://loinc.org", DocumentTypes[j % DocumentTypes.Length]),
#if R4 || R4B || Stu3
                        Status = DocumentReferenceStatus.Current,
                        Content = new List<DocumentReference.ContentComponent>
                        {
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    Url = $"https://test/{id}/attachment.pdf",
                                },
                                Format = new Coding(DocumentSystem, DocumentTypes[j % DocumentTypes.Length]),
                            },
                        },
                        Context = new DocumentReference.ContextComponent()
                        {
                            Period = new Period()
                            {
                                Start = _baseTime.AddHours(-j).ToString("o"),
                                End = _baseTime.AddHours(-j).ToString("o"),
                            },
                        },
#if Stu3
                        Indexed = DateTimeOffset.UtcNow,
#endif
#else
                        Status = DocumentReference.DocumentReferenceStatus.Current,
                        Content = new List<DocumentReference.ContentComponent>
                        {
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    Url = $"https://test/{id}/attachment.pdf",
                                },
                                Profile = new List<DocumentReference.ProfileComponent>
                                {
                                    new DocumentReference.ProfileComponent()
                                    {
                                        Value = new Coding(DocumentSystem, DocumentTypes[j % DocumentTypes.Length]),
                                    },
                                },
                            },
                        },
                        Period = new Period()
                        {
                            Start = _baseTime.AddHours(-j).ToString("o"),
                            End = _baseTime.AddHours(-j).ToString("o"),
                        },
#endif
                    };

                    if (j % 3 == 0)
                    {
                        documentReference.Type.Coding.Add(
                            new Coding(DocumentSystem, DocumentTypes[(j + 1) % DocumentTypes.Length]));
                        documentReference.Content.Add(
                            new DocumentReference.ContentComponent()
                            {
                                Attachment = new Attachment()
                                {
                                    Url = $"https://test/{id}/attachment1.pdf",
                                },
#if R4 || R4B || Stu3
                                Format = new Coding(DocumentSystem, DocumentTypes[(j + 1) % DocumentTypes.Length]),
#else
                                Profile = new List<DocumentReference.ProfileComponent>
                                {
                                    new DocumentReference.ProfileComponent()
                                    {
                                        Value = new Coding(DocumentSystem, DocumentTypes[(j + 1) % DocumentTypes.Length]),
                                    },
                                },
#endif
                            });
                    }

                    var entry = new Bundle.EntryComponent()
                    {
                        Resource = documentReference,
                        Request = new Bundle.RequestComponent()
                        {
                            Method = Bundle.HTTPVerb.POST,
                            Url = KnownResourceTypes.DocumentReference,
                        },
                    };

                    bundle.Entry.Add(entry);
                }
            }

            using var createDocumentReferencesResponse = await TestFhirClient.PostBundleAsync(bundle);
            Assert.Equal(HttpStatusCode.OK, createDocumentReferencesResponse.StatusCode);

            _documentReferences = createDocumentReferencesResponse.Resource.Entry
                .Select(x => (DocumentReference)x.Resource)
                .GroupBy(x => x.Subject.Reference)
                .ToDictionary(x => x.Key, x => x.ToList(), StringComparer.OrdinalIgnoreCase);
        }
    }
}
