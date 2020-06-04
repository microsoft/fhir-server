// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Resources;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Xunit;
using static Hl7.Fhir.Model.Bundle;
using static Hl7.Fhir.Model.OperationOutcome;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [Trait(Traits.Category, Categories.Transaction)]
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.All)]
    public class TransactionTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private readonly TestFhirClient _client;

        public TransactionTests(HttpIntegrationTestFixture fixture)
        {
            _client = fixture.TestFhirClient;
            _client.DeleteAllResources(ResourceType.Patient).Wait();
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.CosmosDb)]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAProperBundle_WhenSubmittingATransactionForCosmosDbDataStore_ThenNotSupportedIsReturned()
        {
            using FhirException ex = await Assert.ThrowsAsync<FhirException>(() => _client.PostBundleAsync(Samples.GetDefaultTransaction().ToPoco<Bundle>()));
            Assert.Equal(HttpStatusCode.MethodNotAllowed, ex.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAProperBundle_WhenSubmittingATransaction_ThenSuccessIsReturnedWithExpectedStatusCodesPerRequests()
        {
            // Insert resources first inorder to test a delete.
            var resource = Samples.GetJsonSample("PatientWithMinimalData");
            using FhirResponse<Patient> response = await _client.CreateAsync(resource.ToPoco<Patient>());

            var insertedId = response.Resource.Id;

            var requestBundle = Samples.GetJsonSample("Bundle-TransactionWithAllValidRoutes").ToPoco<Bundle>();

            // Make the criteria unique so that the tests behave consistently for update
            var updateIdGuid = Guid.NewGuid().ToString();
            requestBundle.Entry[2].Request.Url = requestBundle.Entry[2].Request.Url + updateIdGuid;
            requestBundle.Entry[2].FullUrl = requestBundle.Entry[2].FullUrl + updateIdGuid;
            var updateIdPatient = (Patient)requestBundle.Entry[2].Resource;
            updateIdPatient.Id = updateIdPatient.Id + updateIdGuid;

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

            using var fhirException = await Assert.ThrowsAsync<FhirException>(async () => await _client.PostBundleAsync(requestBundle.ToPoco<Bundle>()));
            Assert.Equal(HttpStatusCode.BadRequest, fhirException.StatusCode);

            string[] expectedDiagnostics = { "Requested operation 'Patient?identifier=123456' is not supported using DELETE." };
            IssueType[] expectedCodeType = { IssueType.Invalid };
            ValidateOperationOutcome(expectedDiagnostics, expectedCodeType, fhirException.OperationOutcome);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAProperTransactionBundle_WhenTransactionExecutionFails_ThenTransactionIsRolledBackAndProperOperationOutComeIsReturned()
        {
            var requestBundle = Samples.GetJsonSample("Bundle-TransactionForRollBack").ToPoco<Bundle>();

            // Make the criteria unique so that the tests behave consistently
            var getIdGuid = Guid.NewGuid().ToString();
            requestBundle.Entry[1].Request.Url = requestBundle.Entry[1].Request.Url + getIdGuid;

            using var fhirException = await Assert.ThrowsAsync<FhirException>(async () => await _client.PostBundleAsync(requestBundle));
            Assert.Equal(HttpStatusCode.NotFound, fhirException.StatusCode);

            string[] expectedDiagnostics = { "Transaction failed on 'GET' for the requested url '/" + requestBundle.Entry[1].Request.Url + "'.", "Resource type 'Patient' with id '12345" + getIdGuid + "' couldn't be found." };
            IssueType[] expectedCodeType = { OperationOutcome.IssueType.Processing, OperationOutcome.IssueType.NotFound };
            ValidateOperationOutcome(expectedDiagnostics, expectedCodeType, fhirException.OperationOutcome);

            // Validate that transaction has rolledback
            Bundle bundle = await _client.SearchAsync(ResourceType.Patient, "family=ADHI");
            Assert.Empty(bundle.Entry);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenABundleWithMutipleEntriesReferringToSameResource_WhenSubmittingATransaction_ThenProperOperationOutComeIsReturned()
        {
            // Insert a resource that has a predefined identifier.
            var resource = Samples.GetJsonSample("PatientWithMinimalData");
            await _client.CreateAsync(resource.ToPoco<Patient>());

            var requestBundle = Samples.GetJsonSample("Bundle-TransactionWithConditionalReferenceReferringToSameResource");

            using var fhirException = await Assert.ThrowsAsync<FhirException>(async () => await _client.PostBundleAsync(requestBundle.ToPoco<Bundle>()));
            Assert.Equal(HttpStatusCode.BadRequest, fhirException.StatusCode);

            string[] expectedDiagnostics = { "Bundle contains multiple entries that refers to the same resource 'Patient?identifier=http:/example.org/fhir/ids|234234'." };
            IssueType[] expectedCodeType = { OperationOutcome.IssueType.Invalid };
            ValidateOperationOutcome(expectedDiagnostics, expectedCodeType, fhirException.OperationOutcome);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAValidBundleWithUnauthorizedUser_WhenSubmittingATransaction_ThenOperationOutcomeWithUnAuthorizedStatusIsReturned()
        {
            TestFhirClient tempClient = _client.CreateClientForClientApplication(TestApplications.WrongAudienceClient);
            var requestBundle = Samples.GetJsonSample("Bundle-TransactionWithValidBundleEntry");

            using var fhirException = await Assert.ThrowsAsync<FhirException>(async () => await tempClient.PostBundleAsync(requestBundle.ToPoco<Bundle>()));
            Assert.Equal(HttpStatusCode.Unauthorized, fhirException.StatusCode);

            string[] expectedDiagnostics = { "Authentication failed." };
            IssueType[] expectedCodeType = { OperationOutcome.IssueType.Login };
            ValidateOperationOutcome(expectedDiagnostics, expectedCodeType, fhirException.OperationOutcome);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAValidBundleWithForbiddenUser_WhenSubmittingATransaction_ThenOperationOutcomeWithForbiddenStatusIsReturned()
        {
            TestFhirClient tempClient = _client.CreateClientForUser(TestUsers.ReadOnlyUser, TestApplications.NativeClient);
            var requestBundle = Samples.GetJsonSample("Bundle-TransactionWithValidBundleEntry");

            using var fhirException = await Assert.ThrowsAsync<FhirException>(async () => await tempClient.PostBundleAsync(requestBundle.ToPoco<Bundle>()));
            Assert.Equal(HttpStatusCode.Forbidden, fhirException.StatusCode);

            string[] expectedDiagnostics = { "Transaction failed on 'POST' for the requested url '/Patient'.", "Authorization failed." };
            IssueType[] expectedCodeType = { OperationOutcome.IssueType.Processing, OperationOutcome.IssueType.Forbidden };
            ValidateOperationOutcome(expectedDiagnostics, expectedCodeType, fhirException.OperationOutcome);
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

            using var fhirException = await Assert.ThrowsAsync<FhirException>(async () => await _client.PostBundleAsync(bundle));

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
            // Insert a resource that has a predefined identifier.
            var resource = Samples.GetJsonSample("PatientWithMinimalData");
            await _client.CreateAsync(resource.ToPoco<Patient>());

            var bundleWithConditionalReference = Samples.GetJsonSample("Bundle-TransactionWithReferenceInResourceBody");

            var bundle = bundleWithConditionalReference.ToPoco<Bundle>();

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
    }
}
