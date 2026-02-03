// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Utility;
using Microsoft.Health.Extensions.Xunit;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Resources;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using static Hl7.Fhir.Model.Bundle;
using static Hl7.Fhir.Model.OperationOutcome;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [Trait(Traits.Category, Categories.BundleTransaction)]
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.All)]
    public class BundleTransactionTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private readonly TestFhirClient _client;

        public BundleTransactionTests(HttpIntegrationTestFixture fixture)
        {
            _client = fixture.TestFhirClient;
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.CosmosDb)]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAProperBundle_WhenSubmittingATransactionForCosmosDbDataStore_ThenNotSupportedIsReturned()
        {
            using FhirClientException ex = await Assert.ThrowsAsync<FhirClientException>(() => _client.PostBundleAsync(Samples.GetDefaultTransaction().ToPoco<Bundle>()));
            Assert.Equal(HttpStatusCode.MethodNotAllowed, ex.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAProperBundle_WhenSubmittingATransaction_ThenSuccessIsReturnedWithExpectedStatusCodesPerRequests()
        {
            var id = Guid.NewGuid().ToString();

            // Insert resources first inorder to test a delete.
            var resource = Samples.GetJsonSample<Patient>("PatientWithMinimalData");
            resource.Identifier[0].Value = id;
            using FhirResponse<Patient> response = await _client.CreateAsync(resource);

            var insertedId = response.Resource.Id;
            var bundleAsString = Samples.GetJson("Bundle-TransactionWithAllValidRoutes");
            bundleAsString = bundleAsString.Replace("http:/example.org/fhir/ids|234234", $"http:/example.org/fhir/ids|{id}");
            bundleAsString = bundleAsString.Replace("234235", Guid.NewGuid().ToString());
            bundleAsString = bundleAsString.Replace("456456", Guid.NewGuid().ToString());

            var parser = new Hl7.Fhir.Serialization.FhirJsonParser();
            var requestBundle = parser.Parse<Bundle>(bundleAsString);

            requestBundle.Entry.Add(new EntryComponent
            {
                Request = new RequestComponent
                {
                    Method = HTTPVerb.DELETE,
                    Url = "Patient/" + insertedId,
                },
            });

            using FhirResponse<Bundle> fhirResponse = await _client.PostBundleAsync(requestBundle);
            Assert.NotNull(fhirResponse);
            Assert.Equal(HttpStatusCode.OK, fhirResponse.StatusCode);

            Assert.True("201".Equals(fhirResponse.Resource.Entry[0].Response.Status), "Create");
            Assert.True("201".Equals(fhirResponse.Resource.Entry[1].Response.Status), "Conditional Create");
            Assert.True("201".Equals(fhirResponse.Resource.Entry[2].Response.Status), "Update");
            Assert.True("201".Equals(fhirResponse.Resource.Entry[3].Response.Status), "Conditional Update");
            Assert.True("200".Equals(fhirResponse.Resource.Entry[4].Response.Status), "Get");
            Assert.True("200".Equals(fhirResponse.Resource.Entry[5].Response.Status), "Get");
            Assert.True("204".Equals(fhirResponse.Resource.Entry[6].Response.Status), "Delete");
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenABundleWithInvalidRoutes_WhenSubmittingATransaction_ThenBadRequestExceptionIsReturnedWithProperOperationOutCome()
        {
            var requestBundle = Samples.GetJsonSample("Bundle-TransactionWithInvalidProcessingRoutes");

            using var fhirException = await Assert.ThrowsAsync<FhirClientException>(async () => await _client.PostBundleAsync(requestBundle.ToPoco<Bundle>()));
            Assert.Equal(HttpStatusCode.BadRequest, fhirException.StatusCode);

            string[] expectedDiagnostics = { "Requested operation 'Patient?identifier=123456' is not supported using DELETE." };
            IssueType[] expectedCodeType = { IssueType.Invalid };
            ValidateOperationOutcome(expectedDiagnostics, expectedCodeType, fhirException.OperationOutcome);
        }

        [RetryTheory]
        [Trait(Traits.Priority, Priority.One)]
        [InlineData(FhirBundleProcessingLogic.Parallel)]
        [InlineData(FhirBundleProcessingLogic.Sequential)]
        public async Task GivenAProperTransactionBundle_WhenTransactionExecutionFails_ThenTransactionIsRolledBackAndProperOperationOutComeIsReturned(FhirBundleProcessingLogic processingLogic)
        {
            var requestBundle = Samples.GetJsonSample("Bundle-TransactionForRollBack").ToPoco<Bundle>();

            // Make the criteria unique so that the tests behave consistently
            var getIdGuid = Guid.NewGuid().ToString();
            requestBundle.Entry[1].Request.Url = requestBundle.Entry[1].Request.Url + getIdGuid;

            using var fhirException = await Assert.ThrowsAsync<FhirClientException>(async () => await _client.PostBundleAsync(
                requestBundle,
                new FhirBundleOptions() { BundleProcessingLogic = processingLogic }));
            Assert.Equal(HttpStatusCode.NotFound, fhirException.StatusCode);

            string[] expectedDiagnostics = { "Transaction failed on 'GET' for the requested url '/" + requestBundle.Entry[1].Request.Url + "'.", "Resource type 'Patient' with id '12345" + getIdGuid + "' couldn't be found." };
            IssueType[] expectedCodeType = { OperationOutcome.IssueType.Processing, OperationOutcome.IssueType.NotFound };
            ValidateOperationOutcome(expectedDiagnostics, expectedCodeType, fhirException.OperationOutcome);

            // Validate that transaction has rolledback
            Bundle bundle = await _client.SearchAsync(ResourceType.Patient, "family=TransactionRollback");
            Assert.Empty(bundle.Entry);
        }

        [RetryTheory]
        [Trait(Traits.Priority, Priority.One)]
        [InlineData(FhirBundleProcessingLogic.Parallel, false)]
        [InlineData(FhirBundleProcessingLogic.Sequential, false)]
        [InlineData(FhirBundleProcessingLogic.Sequential, true)]
        public async Task GivenATransactionBundleWithDelete_WhenTransactionExecutionFails_ThenTransactionIsRolledBackAndNoOperationCompletes(FhirBundleProcessingLogic processingLogic, bool hardDeletes)
        {
            await TestBundleTransactionsRollbacksWithDeletesAsync(processingLogic, hardDeletes: hardDeletes, CancellationToken.None);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenABundleWithMutipleEntriesReferringToSameResource_WhenSubmittingATransaction_ThenProperOperationOutComeIsReturned()
        {
            var id = Guid.NewGuid().ToString();

            // Insert resources first inorder to test a delete.
            var resource = Samples.GetJsonSample<Patient>("PatientWithMinimalData");
            resource.Identifier[0].Value = id;
            using FhirResponse<Patient> response = await _client.CreateAsync(resource);

            var bundleAsString = Samples.GetJson("Bundle-TransactionWithConditionalReferenceReferringToSameResource");
            bundleAsString = bundleAsString.Replace("http:/example.org/fhir/ids|234234", $"http:/example.org/fhir/ids|{id}");
            var parser = new Hl7.Fhir.Serialization.FhirJsonParser();
            var bundle = parser.Parse<Bundle>(bundleAsString);

            using var fhirException = await Assert.ThrowsAsync<FhirClientException>(async () => await _client.PostBundleAsync(bundle));
            Assert.Equal(HttpStatusCode.BadRequest, fhirException.StatusCode);

            string[] expectedDiagnostics = { $"Bundle contains multiple entries that refers to the same resource 'Patient?identifier=http:/example.org/fhir/ids|{id}'." };
            IssueType[] expectedCodeType = { OperationOutcome.IssueType.Invalid };
            ValidateOperationOutcome(expectedDiagnostics, expectedCodeType, fhirException.OperationOutcome);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        [Trait(Traits.Category, Categories.Authorization)]
        public async Task GivenAValidBundleWithUnauthorizedUser_WhenSubmittingATransaction_ThenOperationOutcomeWithUnAuthorizedStatusIsReturned()
        {
            TestFhirClient tempClient = _client.CreateClientForClientApplication(TestApplications.WrongAudienceClient);
            var requestBundle = Samples.GetJsonSample("Bundle-TransactionWithValidBundleEntry");

            using var fhirException = await Assert.ThrowsAsync<FhirClientException>(async () => await tempClient.PostBundleAsync(requestBundle.ToPoco<Bundle>()));
            Assert.Equal(HttpStatusCode.Unauthorized, fhirException.StatusCode);

            string[] expectedDiagnostics = { "Authentication failed." };
            IssueType[] expectedCodeType = { OperationOutcome.IssueType.Login };
            ValidateOperationOutcome(expectedDiagnostics, expectedCodeType, fhirException.OperationOutcome);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        [Trait(Traits.Category, Categories.Authorization)]
        public async Task GivenAValidBundleWithForbiddenUser_WhenSubmittingATransaction_ThenOperationOutcomeWithForbiddenStatusIsReturned()
        {
            TestFhirClient tempClient = _client.CreateClientForClientApplication(TestApplications.ReadOnlyUser);

            var id = Guid.NewGuid().ToString();
            var bundleAsString = Samples.GetJson("Bundle-TransactionWithValidBundleEntry");
            bundleAsString = bundleAsString.Replace("identifier=234234", $"identifier={id}");
            var parser = new Hl7.Fhir.Serialization.FhirJsonParser();
            var requestBundle = parser.Parse<Bundle>(bundleAsString);

            using var fhirException = await Assert.ThrowsAsync<FhirClientException>(async () => await tempClient.PostBundleAsync(requestBundle));
            Assert.Equal(HttpStatusCode.Forbidden, fhirException.StatusCode);

            string[] expectedDiagnostics = { "Transaction failed on 'POST' for the requested url '/Patient'.", "Authorization failed." };
            IssueType[] expectedCodeType = { OperationOutcome.IssueType.Processing, OperationOutcome.IssueType.Forbidden };
            ValidateOperationOutcome(expectedDiagnostics, expectedCodeType, fhirException.OperationOutcome);
        }

        [Theory]
        [Trait(Traits.Priority, Priority.One)]
        [InlineData(FhirBundleProcessingLogic.Parallel)]
        [InlineData(FhirBundleProcessingLogic.Sequential)]
        public async Task GivenABundleWithSingleGeneratedId_WhenSubmittingATransaction_ThenTheSingleGeneratedIDIsResolvedProperly(FhirBundleProcessingLogic processingLogic)
        {
            // In this test, we create a bundle with multiple resources referencing to a single generated ID.
            // The bundle includes a Patient, Consent and Observation.
            // Each resource is created with a POST request, and the bundle is processed as a transaction (sequential or parallel).
            // The test verifies that all resources are created successfully, and their references are resolved correctly.

            var bundle = new Bundle
            {
                Type = BundleType.Transaction,
                Entry = new List<EntryComponent>
                {
                    new()
                    {
                        FullUrl = "urn:uuid:patient",
                        Resource = new Patient
                        {
                            Id = string.Empty,
                            Active = true,
                            Name = new List<HumanName>
                            {
                                new HumanName
                                {
                                    Family = "Doe",
                                    Given = new[] { "John" },
                                },
                            },
                            Identifier = new List<Identifier>
                            {
                                new Identifier
                                {
                                    System = "http://example.org/fhir/ids",
                                    Value = "12345",
                                },
                            },
                        },
                        Request = new RequestComponent
                        {
                            Method = HTTPVerb.POST,
                            Url = "Patient",
                        },
                    },
                    new()
                    {
                        Resource = new Observation
                        {
                            Status = ObservationStatus.Final,
                            Code = new CodeableConcept("http://loinc.org", "1234-5", "Blood Pressure"),
                            Subject = new ResourceReference
                            {
#if !Stu3
                                Type = "Patient",
#endif
                                Reference = "urn:uuid:patient",
                            },
                        },
                        Request = new RequestComponent
                        {
                            Method = HTTPVerb.POST,
                            Url = "Observation",
                        },
                    },
                    new()
                    {
                        Resource = new Consent
                        {
                            Status = Consent.ConsentState.Active,
                            Category = new List<CodeableConcept>
                            {
                                new CodeableConcept("http://terminology.hl7.org/CodeSystem/consentcategorycodes", "admission"),
                            },
#if !R5
                            Patient = new ResourceReference
                            {
#if !Stu3
                                Type = "Patient",
#endif
                                Reference = "urn:uuid:patient",
                            },
#else
                            Subject = new ResourceReference
                            {
                                Reference = "urn:uuid:patient",
                            },
#endif
#if !Stu3 && !R5
                            Scope = new CodeableConcept("http://terminology.hl7.org/CodeSystem/consentscope", "adr"),
#endif
                        },
                        Request = new RequestComponent
                        {
                            Method = HTTPVerb.POST,
                            Url = "Consent",
                        },
                    },
                },
            };

            FhirResponse<Bundle> bundleResponse = await _client.PostBundleAsync(bundle, new FhirBundleOptions() { BundleProcessingLogic = processingLogic });

            Assert.True(bundleResponse.Resource.Entry[0].Resource.TypeName == ResourceType.Patient.GetLiteral(), "First entry should be a Patient resource.");
            Assert.True(bundleResponse.Resource.Entry[1].Resource.TypeName == ResourceType.Observation.GetLiteral(), "Second entry should be an Observation resource.");
            Assert.True(bundleResponse.Resource.Entry[2].Resource.TypeName == ResourceType.Consent.GetLiteral(), "Fourth entry should be an Consent resource.");

            string patientId = bundleResponse.Resource.Entry[0].Resource.Id;
            string observationId = bundleResponse.Resource.Entry[1].Resource.Id;
            string consentId = bundleResponse.Resource.Entry[2].Resource.Id;

            Assert.True(Guid.TryParse(patientId, out _), "Patient ID should be a valid GUID.");
            Assert.True(Guid.TryParse(observationId, out _), "Observation ID should be a valid GUID.");
            Assert.True(Guid.TryParse(consentId, out _), "Consent ID should be a valid GUID.");

            // Observation references.
            string observationPatientReference = bundleResponse.Resource.Entry[1].Resource.GetAllChildren<ResourceReference>().ToArray()[0].Reference;
            Assert.Equal($"Patient/{patientId}", observationPatientReference);

            // Consent refereces.
            string consentPatientReference = bundleResponse.Resource.Entry[2].Resource.GetAllChildren<ResourceReference>().FirstOrDefault()?.Reference;
            Assert.Equal($"Patient/{patientId}", consentPatientReference);
        }

        [Theory]
        [Trait(Traits.Priority, Priority.One)]
        [InlineData(FhirBundleProcessingLogic.Parallel)]
        [InlineData(FhirBundleProcessingLogic.Sequential)]
        public async Task GivenABundleWithMultipleDynamicReferences_WhenSubmittingATransaction_ThenAllReferencesAreResolvedProperly(FhirBundleProcessingLogic processingLogic)
        {
            // In this test, we create a bundle with multiple resources that reference each other, forcing the generation of multiple IDs.
            // The bundle includes a Patient, Consent, Observation, Device, Practitioner, and Organization.
            // Each resource is created with a POST request, and the bundle is processed as a transaction (sequential or parallel).
            // The test verifies that all resources are created successfully, and their references are resolved correctly.

            // A transaction may include references from one resource to another in the bundle, including circular references where resources refer to each other.
            // UUID should be used (urn:uuid:...) as a reference to a resource in the Bundle. The server will process all urn:uuid and generate a new ID for each resource,
            // which will be used in the references. UUID will only be created if the resource is submitted with a POST method (check BundleHandler logic).

            var bundle = new Bundle
            {
                Type = BundleType.Transaction,
                Entry = new List<EntryComponent>
                {
                    new()
                    {
                        FullUrl = "urn:uuid:patient",
                        Resource = new Patient
                        {
                            Id = string.Empty,
                            Active = true,
                            Name = new List<HumanName>
                            {
                                new HumanName
                                {
                                    Family = "Doe",
                                    Given = new[] { "John" },
                                },
                            },
                            Identifier = new List<Identifier>
                            {
                                new Identifier
                                {
                                    System = "http://example.org/fhir/ids",
                                    Value = "12345",
                                },
                            },
                        },
                        Request = new RequestComponent
                        {
                            Method = HTTPVerb.POST,
                            Url = "Patient",
                        },
                    },
                    new()
                    {
                        FullUrl = "urn:uuid:consent",
                        Resource = new Consent
                        {
                            Status = Consent.ConsentState.Active,
                            Category = new List<CodeableConcept>
                            {
                                new CodeableConcept("http://terminology.hl7.org/CodeSystem/consentcategorycodes", "admission"),
                            },
#if !R5
                            Patient = new ResourceReference
                            {
#if !Stu3
                                Type = "Patient",
#endif
                                Reference = "urn:uuid:patient",
                            },
#else
                            Subject = new ResourceReference
                            {
                                Reference = "urn:uuid:patient",
                            },
#endif
#if !Stu3 && !R5
                            Scope = new CodeableConcept("http://terminology.hl7.org/CodeSystem/consentscope", "adr"),
#endif
                        },
                        Request = new RequestComponent
                        {
                            Method = HTTPVerb.POST,
                            Url = "Consent",
                        },
                    },
                    new()
                    {
                        FullUrl = "urn:uuid:observation",
                        Resource = new Observation
                        {
                            Status = ObservationStatus.Final,
                            Code = new CodeableConcept("http://loinc.org", "1234-5", "Blood Pressure"),
                            Subject = new ResourceReference
                            {
#if !Stu3
                                Type = "Patient",
#endif
                                Reference = "urn:uuid:patient",
                            },
                            Performer = new List<ResourceReference>
                            {
                                new ResourceReference
                                {
#if !Stu3
                                    Type = "Practitioner",
#endif
                                    Reference = "urn:uuid:performer",
                                },
                                new ResourceReference
                                {
#if !Stu3
                                    Type = "Organization",
#endif
                                    Reference = "urn:uuid:organization",
                                },
                            },
                        },
                        Request = new RequestComponent
                        {
                            Method = HTTPVerb.POST,
                            Url = "Observation",
                        },
                    },
                    new()
                    {
                        FullUrl = "urn:uuid:device",
                        Resource = new Device
                        {
                            Status = Device.FHIRDeviceStatus.Active,
#if !R5
                            Patient = new ResourceReference
                            {
#if !Stu3
                                Type = "Patient",
#endif
                                Reference = "urn:uuid:patient",
                            },
#else
                            Parent = new ResourceReference
                            {
                                Type = "Patient",
                                Reference = "urn:uuid:patient",
                            },
#endif
                        },
                        Request = new RequestComponent
                        {
                            Method = HTTPVerb.POST,
                            Url = "Device",
                        },
                    },
                    new()
                    {
                        FullUrl = "urn:uuid:performer",
                        Resource = new Practitioner
                        {
                            Name = new List<HumanName>
                            {
                                new HumanName
                                {
                                    Family = "Doe",
                                    Given = new[] { "Jane" },
                                },
                            },
                        },
                        Request = new RequestComponent
                        {
                            Method = HTTPVerb.POST,
                            Url = "Practitioner",
                        },
                    },
                    new()
                    {
                        FullUrl = "urn:uuid:organization",
                        Resource = new Organization
                        {
                            Name = "Test Organization",
                            Identifier = new List<Identifier>
                            {
                                new Identifier
                                {
                                    System = "http://example.org/fhir/ids",
                                    Value = "org-12345",
                                },
                            },
                        },
                        Request = new RequestComponent
                        {
                            Method = HTTPVerb.POST,
                            Url = "Organization",
                        },
                    },
                },
            };

            FhirResponse<Bundle> bundleResponse = await _client.PostBundleAsync(bundle, new FhirBundleOptions() { BundleProcessingLogic = processingLogic });

            Assert.True(bundleResponse.Resource.Entry[0].Resource.TypeName == ResourceType.Patient.GetLiteral(), "First entry should be a Patient resource.");
            Assert.True(bundleResponse.Resource.Entry[1].Resource.TypeName == ResourceType.Consent.GetLiteral(), "Second entry should be a Consent resource.");
            Assert.True(bundleResponse.Resource.Entry[2].Resource.TypeName == ResourceType.Observation.GetLiteral(), "Third entry should be an Observation resource.");
            Assert.True(bundleResponse.Resource.Entry[3].Resource.TypeName == ResourceType.Device.GetLiteral(), "Fourth entry should be a Device resource.");
            Assert.True(bundleResponse.Resource.Entry[4].Resource.TypeName == ResourceType.Practitioner.GetLiteral(), "Fifth entry should be a Practitioner resource.");
            Assert.True(bundleResponse.Resource.Entry[5].Resource.TypeName == ResourceType.Organization.GetLiteral(), "Sixth entry should be an Organization resource.");

            string patientId = bundleResponse.Resource.Entry[0].Resource.Id;
            string consentId = bundleResponse.Resource.Entry[1].Resource.Id;
            string observationId = bundleResponse.Resource.Entry[2].Resource.Id;
            string deviceId = bundleResponse.Resource.Entry[3].Resource.Id;
            string practitionerId = bundleResponse.Resource.Entry[4].Resource.Id;
            string organizationId = bundleResponse.Resource.Entry[5].Resource.Id;

            Assert.True(Guid.TryParse(patientId, out _), "Patient ID should be a valid GUID.");
            Assert.True(Guid.TryParse(consentId, out _), "Consent ID should be a valid GUID.");
            Assert.True(Guid.TryParse(observationId, out _), "Observation ID should be a valid GUID.");
            Assert.True(Guid.TryParse(deviceId, out _), "Device ID should be a valid GUID.");
            Assert.True(Guid.TryParse(practitionerId, out _), "Practitioner ID should be a valid GUID.");
            Assert.True(Guid.TryParse(organizationId, out _), "Organization ID should be a valid GUID.");

            // Consent refereces.
            string consentPatientReference = bundleResponse.Resource.Entry[1].Resource.GetAllChildren<ResourceReference>().FirstOrDefault()?.Reference;
            Assert.Equal($"Patient/{patientId}", consentPatientReference);

            // Observation references.
            string observationPatientReference = bundleResponse.Resource.Entry[2].Resource.GetAllChildren<ResourceReference>().ToArray()[0].Reference;
            Assert.Equal($"Patient/{patientId}", observationPatientReference);

            string observationPractitionerReference = bundleResponse.Resource.Entry[2].Resource.GetAllChildren<ResourceReference>().ToArray()[1].Reference;
            Assert.Equal($"Practitioner/{practitionerId}", observationPractitionerReference);

            string observationOrganizationReference = bundleResponse.Resource.Entry[2].Resource.GetAllChildren<ResourceReference>().ToArray()[2].Reference;
            Assert.Equal($"Organization/{organizationId}", observationOrganizationReference);

            // Device references.
            string devicePatientReference = bundleResponse.Resource.Entry[3].Resource.GetAllChildren<ResourceReference>().FirstOrDefault()?.Reference;
            Assert.Equal($"Patient/{patientId}", devicePatientReference);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenABundleWithInvalidConditionalReferenceInResourceBody_WhenSubmittingATransaction_ThenProperOperationOutComeIsReturned()
        {
            string patientId = Guid.NewGuid().ToString();

            var observation = new Observation
            {
                Subject = new ResourceReference
                {
                    Reference = "Patient?identifier=http:/example.org/fhir/ids|" + patientId,
                },
            };

            var bundle = new Bundle
            {
                Type = BundleType.Transaction,
                Entry = new List<EntryComponent>
                {
                    new EntryComponent
                    {
                        Resource = observation,
                        Request = new RequestComponent
                        {
                            Method = HTTPVerb.POST,
                            Url = "Observation",
                        },
                    },
                },
            };

            using var fhirException = await Assert.ThrowsAsync<FhirClientException>(async () => await _client.PostBundleAsync(bundle));

            Assert.Equal(HttpStatusCode.BadRequest, fhirException.StatusCode);

            string[] expectedDiagnostics = { "Given conditional reference 'Patient?identifier=http:/example.org/fhir/ids|" + patientId + "' does not resolve to a resource." };
            IssueType[] expectedCodeType = { IssueType.Invalid };
            ValidateOperationOutcome(expectedDiagnostics, expectedCodeType, fhirException.OperationOutcome);
        }

        private static void ValidateOperationOutcome(string[] expectedDiagnostics, IssueType[] expectedCodeType, OperationOutcome operationOutcome)
        {
            Assert.NotNull(operationOutcome?.Id);
            Assert.NotEmpty(operationOutcome?.Issue);

            Assert.Equal(expectedCodeType.Length, operationOutcome.Issue.Count);
            Assert.Equal(expectedDiagnostics.Length, operationOutcome.Issue.Count);

            for (int iter = 0; iter < operationOutcome.Issue.Count; iter++)
            {
                Assert.Equal(expectedCodeType[iter], operationOutcome.Issue[iter].Code);
                Assert.Equal(OperationOutcome.IssueSeverity.Error, operationOutcome.Issue[iter].Severity);
                Assert.Equal(expectedDiagnostics[iter], operationOutcome.Issue[iter].Diagnostics);
            }
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenABundleWithForeignReferenceInResourceBody_WhenSubmittingATransaction_ThenReferenceShouldNotBeResolvedAndProcessShouldSucceed()
        {
            var requestBundle = Samples.GetJsonSample("Bundle-TransactionWithForeignReferenceInResourceBody");

            using FhirResponse<Bundle> fhirResponse = await _client.PostBundleAsync(requestBundle.ToPoco<Bundle>());

            Assert.NotNull(fhirResponse);
            Assert.Equal(HttpStatusCode.OK, fhirResponse.StatusCode);

            foreach (var entry in fhirResponse.Resource.Entry)
            {
                IEnumerable<ResourceReference> references = entry.Resource.GetAllChildren<ResourceReference>();
                foreach (var reference in references)
                {
                    // Asserting the conditional reference value before resolution
                    Assert.Equal("urn:uuid:4a089b8a-b0a0-46a9-92da-c8b653aa2e73", reference.Reference);
                }
            }
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenATransactionBundleReferencesInResourceBody_WhenSuccessfulExecution_ReferencesAreResolvedCorrectlyAsync()
        {
            var id = Guid.NewGuid().ToString();

            // Insert a resource that has a predefined identifier.
            var resource = Samples.GetJsonSample<Patient>("PatientWithMinimalData");
            resource.Identifier[0].Value = id;
            await _client.CreateAsync(resource);

            var bundleAsString = Samples.GetJson("Bundle-TransactionWithReferenceInResourceBody");
            bundleAsString = bundleAsString.Replace("http:/example.org/fhir/ids|234234", $"http:/example.org/fhir/ids|{id}");
            var parser = new Hl7.Fhir.Serialization.FhirJsonParser();
            var bundle = parser.Parse<Bundle>(bundleAsString);

            using FhirResponse<Bundle> fhirResponseForReferenceResolution = await _client.PostBundleAsync(bundle);

            Assert.NotNull(fhirResponseForReferenceResolution);
            Assert.Equal(HttpStatusCode.OK, fhirResponseForReferenceResolution.StatusCode);

            foreach (var entry in fhirResponseForReferenceResolution.Resource.Entry)
            {
                IEnumerable<ResourceReference> references = entry.Resource.GetAllChildren<ResourceReference>();

                foreach (var reference in references)
                {
                    // Asserting the conditional reference value after resolution
                    Assert.True(reference.Reference.Contains("/", StringComparison.Ordinal));

                    // Also asserting that the conditional reference is resolved correctly
                    Assert.False(reference.Reference.Contains("?", StringComparison.Ordinal));
                }
            }
        }

        [Theory]
        [Trait(Traits.Priority, Priority.One)]
        [InlineData(FhirBundleProcessingLogic.Parallel)]
        [InlineData(FhirBundleProcessingLogic.Sequential)]
        public async Task GivenATransactionWithConditionalCreateAndReference_WhenExecutedASecondTime_ReferencesAreResolvedCorrectlyAsync(FhirBundleProcessingLogic processingLogic)
        {
            var bundleWithConditionalReference = Samples.GetJsonSample("Bundle-TransactionWithConditionalCreateAndReference");

            var bundle = bundleWithConditionalReference.ToPoco<Bundle>();
            var patient = bundle.Entry.First().Resource.ToResourceElement().ToPoco<Patient>();
            var patientIdentifier = Guid.NewGuid().ToString();

            patient.Identifier.First().Value = patientIdentifier;
            bundle.Entry.First().Request.IfNoneExist = $"identifier=|{patientIdentifier}";

            FhirResponse<Bundle> bundleResponse1 = await _client.PostBundleAsync(bundle, new FhirBundleOptions() { BundleProcessingLogic = processingLogic });

            var patientId = bundleResponse1.Resource.Entry.First().Resource.Id;
            ValidateReferenceToPatient("Bundle 1", bundleResponse1.Resource.Entry[1].Resource, patientId, bundleResponse1);

            FhirResponse<Bundle> bundleResponse2 = await _client.PostBundleAsync(bundle, new FhirBundleOptions() { BundleProcessingLogic = processingLogic });
            ValidateReferenceToPatient("Bundle 2", bundleResponse2.Resource.Entry[1].Resource, patientId, bundleResponse2);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenATransactionWithConditionalUpdateAndReference_WhenExecutedASecondTime_ReferencesAreResolvedCorrectlyAsync()
        {
            var bundleWithConditionalReference = Samples.GetJsonSample("Bundle-TransactionWithConditionalUpdateAndReference");

            var bundle = bundleWithConditionalReference.ToPoco<Bundle>();
            var patient = bundle.Entry.First().Resource.ToResourceElement().ToPoco<Patient>();
            var patientIdentifier = Guid.NewGuid().ToString();

            patient.Identifier.First().Value = patientIdentifier;
            bundle.Entry.First().Request.Url = $"Patient?identifier=|{patientIdentifier}";

            FhirResponse<Bundle> bundleResponse1 = await _client.PostBundleAsync(bundle);

            var patientId = bundleResponse1.Resource.Entry.First().Resource.Id;
            ValidateReferenceToPatient("Bundle 1", bundleResponse1.Resource.Entry[1].Resource, patientId, bundleResponse1);

            patient.Text = new Narrative
            {
                Status = Narrative.NarrativeStatus.Generated,
                Div = $"<div>Content Updated</div>",
            };

            FhirResponse<Bundle> bundleResponse2 = await _client.PostBundleAsync(bundle);

            Assert.Equal(patientId, bundleResponse2.Resource.Entry[0].Resource.Id);
            Assert.Equal("2", bundleResponse2.Resource.Entry[0].Resource.Meta.VersionId);
            ValidateReferenceToPatient("Bundle 2", bundleResponse2.Resource.Entry[1].Resource, patientId, bundleResponse2);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenATransactionWithAllowedParameter_WhenExecuted_ThenNoExceptionsAreThrown()
        {
            // Create resource
            var parser = new Hl7.Fhir.Serialization.FhirJsonParser();
            var bundleAsString = Samples.GetJson("Bundle-TransactionWithMetaHistory");
            var requestBundle = parser.Parse<Bundle>(bundleAsString);

            using FhirResponse<Bundle> fhirResponse = await _client.PostBundleAsync(requestBundle);
            Assert.NotNull(fhirResponse);
            Assert.Equal(HttpStatusCode.OK, fhirResponse.StatusCode);

            // Status could be 201 (Created) or 200 (OK) based on whether the resource is created or updated.
            var status = int.Parse(fhirResponse.Resource.Entry[0].Response.Status);
            Assert.True(status == 201 || status == 200, "Create");

            // Update resource
            bundleAsString = bundleAsString.Replace("\"metaHistoryTestTag\"", "\"metaHistoryTestTag2\"");

            requestBundle = parser.Parse<Bundle>(bundleAsString);

            using FhirResponse<Bundle> fhirResponse2 = await _client.PostBundleAsync(requestBundle);
            Assert.NotNull(fhirResponse2);
            Assert.Equal(HttpStatusCode.OK, fhirResponse2.StatusCode);
            Assert.True("200".Equals(fhirResponse2.Resource.Entry[0].Response.Status), "Update");

            // Check history
            var historyBundle = await _client.ReadHistoryAsync(ResourceType.MedicationRequest, fhirResponse.Resource.Entry[0].Resource.Id);
            Assert.True(historyBundle.Resource.Entry.Count == 1, "History count");
        }

        private static void ValidateReferenceToPatient(
            string prefix,
            Resource resource,
            string patientId,
            FhirResponse<Bundle> response)
        {
            IEnumerable<ResourceReference> imagingStudyReferences = resource.GetAllChildren<ResourceReference>();
            bool foundReference = false;
            string expected = $"Patient/{patientId}";

            foreach (var reference in imagingStudyReferences)
            {
                if (reference.Reference.StartsWith("Patient"))
                {
                    string current = reference.Reference;

                    Assert.True(
                        string.Equals(expected, current, StringComparison.Ordinal),
                        userMessage: $"{prefix} - Expected patient reference ({expected}) is different than the current ({current}). Details: {response.GetFhirResponseDetailsAsJson()}");

                    foundReference = true;
                }
            }

            Assert.True(foundReference, "Patient reference wasn't found.");
        }

        private async Task TestBundleTransactionsRollbacksWithDeletesAsync(FhirBundleProcessingLogic processingLogic, bool hardDeletes, CancellationToken cancellationToken)
        {
            // 1 - Load information from sample.
            var bundleJson = Samples.GetJsonSample("Bundle-TransactionForRollBackWithDelete");
            Bundle bundle = bundleJson.ToPoco<Bundle>();

            string patientId0 = bundle.Entry[0].Request.Url.Split("/")[1];
            string patientId1 = bundle.Entry[1].Request.Url.Split("/")[1];
            string patientId2 = bundle.Entry[2].Resource.Id;
            string observationId0 = bundle.Entry[3].Resource.Id;

            if (hardDeletes)
            {
                // Modify the delete requests to be hard deletes.
                foreach (var entry in bundle.Entry)
                {
                    if (entry.Request.Method == HTTPVerb.DELETE)
                    {
                        entry.Request.Url = $"{entry.Request.Url}?_hardDelete=true";
                    }
                }
            }

            // 2 - Create resource that will be attempted to be deleted in the transaction.
            Patient patient0 = new Patient
            {
                Id = patientId0,
                Active = true,
                Name = new List<HumanName>
                {
                    new HumanName
                    {
                        Family = "Doe",
                        Given = new[] { "John" },
                    },
                },
                Identifier = new List<Identifier>
                {
                    new Identifier
                    {
                        System = "http://example.org/fhir/ids",
                        Value = patientId0,
                    },
                },
            };

            Patient patient1 = new Patient
            {
                Id = patientId1,
                Active = true,
                Name = new List<HumanName>
                {
                    new HumanName
                    {
                        Family = "John",
                        Given = new[] { "Doe" },
                    },
                },
                Identifier = new List<Identifier>
                {
                    new Identifier
                    {
                        System = "http://example.org/fhir/ids",
                        Value = patientId0,
                    },
                },
            };

            // Creating Patient-0
            FhirResponse<Patient> putPatient0Response = await _client.UpdateAsync($"Patient/{patientId0}", patient0, cancellationToken: cancellationToken);
            DateTimeOffset? creationTimePatient0 = putPatient0Response.Resource.Meta.LastUpdated;
            Assert.True(putPatient0Response.Response.IsSuccessStatusCode, "The creation of the Patient is expected, as this patient is used as part of the validations.");
            Assert.True(patientId0 == putPatient0Response.Resource.Id, $"Patient ID is expected to be created as '{patientId0}'.");

            // Creating Patient-1
            FhirResponse<Patient> putPatient1Response = await _client.UpdateAsync($"Patient/{patientId1}", patient1, cancellationToken: cancellationToken);
            DateTimeOffset? creationTimePatient1 = putPatient1Response.Resource.Meta.LastUpdated;
            Assert.True(putPatient1Response.Response.IsSuccessStatusCode, "The creation of the Patient is expected, as this patient is used as part of the validations.");
            Assert.True(patientId1 == putPatient1Response.Resource.Id, $"Patient ID is expected to be created as '{patientId1}'.");

            // 3 - Execute transaction that is expected to fail.
            using var fhirException = await Assert.ThrowsAsync<FhirClientException>(
                async () => await _client.PostBundleAsync(
                    bundle,
                    new FhirBundleOptions() { BundleProcessingLogic = processingLogic },
                    cancellationToken));

            // Bug 182314: Standardize status code returned when a bundle fails.
            if (processingLogic == FhirBundleProcessingLogic.Sequential && !hardDeletes)
            {
                Assert.Equal(HttpStatusCode.NotFound, fhirException.Response.StatusCode);
            }
            else
            {
                Assert.Equal(HttpStatusCode.BadRequest, fhirException.Response.StatusCode);
            }

            // 4 - Validate if Patients still exist.
            FhirResponse<Patient> getPatient0Response = null;
            try
            {
                getPatient0Response = await _client.ReadAsync<Patient>($"Patient/{patientId0}", cancellationToken: cancellationToken);
            }
            catch (FhirClientException e)
            {
                Assert.Fail($"The Patient is expected to still exist, as the transaction is expected to be rolled back. Exception: {e.Message}");
            }

            if (getPatient0Response == null)
            {
                Assert.Fail($"The response of '{nameof(getPatient0Response)}' is null.");
            }
            else
            {
                Assert.True(getPatient0Response.Response.IsSuccessStatusCode, "The Patient is expected to still exist, as the transaction is expected to be rolled back.");
                DateTimeOffset? lastUpdated = getPatient0Response.Resource.Meta.LastUpdated;
                Assert.True(patientId0 == putPatient0Response.Resource.Id, $"Patient ID is expected to be created as '{patientId0}'.");
                Assert.True(creationTimePatient0 == lastUpdated, $"Meta.LastUpdate is expected to be the same. Left: '{creationTimePatient0?.ToString("o")}'. Right: '{lastUpdated?.ToString("o")}'");
            }

            FhirResponse<Patient> getPatient1Response = null;
            try
            {
                getPatient1Response = await _client.ReadAsync<Patient>($"Patient/{patientId1}", cancellationToken: cancellationToken);
            }
            catch (FhirClientException e)
            {
                Assert.Fail($"The Patient is expected to still exist, as the transaction is expected to be rolled back. Exception: {e.Message}");
            }

            if (getPatient1Response == null)
            {
                Assert.Fail($"The response of '{nameof(getPatient1Response)}' is null.");
            }
            else
            {
                Assert.True(getPatient1Response.Response.IsSuccessStatusCode, "The Patient is expected to still exist, as the transaction is expected to be rolled back.");
                DateTimeOffset? lastUpdated = getPatient1Response.Resource.Meta.LastUpdated;
                Assert.True(patientId1 == putPatient1Response.Resource.Id, $"Patient ID is expected to be created as '{patientId1}'.");
                Assert.True(creationTimePatient1 == lastUpdated, $"Meta.LastUpdate is expected to be the same. Left: '{creationTimePatient1?.ToString("o")}'. Right: '{lastUpdated?.ToString("o")}'");
            }

            // 5 - Validate if other resources do not exist.
            try
            {
                FhirResponse<Patient> getPatient2Response = await _client.ReadAsync<Patient>($"Patient/{patientId2}", cancellationToken: cancellationToken);
                Assert.Fail($"The Patient '{patientId2}' is not expected to exist, as the transaction is expected to be rolled back.");
            }
            catch (FhirClientException e)
            {
                Assert.True(e.StatusCode == HttpStatusCode.NotFound, $"The Patient is not expected to exist, as the transaction is expected to be rolled back.");
            }

            try
            {
                FhirResponse<Observation> getObservation0Response = await _client.ReadAsync<Observation>($"Observation/{observationId0}", cancellationToken: cancellationToken);
                Assert.Fail($"The Observation '{observationId0}' is not expected to exist, as the transaction is expected to be rolled back.");
            }
            catch (FhirClientException e)
            {
                Assert.True(e.StatusCode == HttpStatusCode.NotFound, $"The Observation is not expected to exist, as the transaction is expected to be rolled back.");
            }
        }
    }
}
