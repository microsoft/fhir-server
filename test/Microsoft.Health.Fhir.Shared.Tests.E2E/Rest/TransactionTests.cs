// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using System.Net;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Api.Features.Resources;
using Microsoft.Health.Fhir.Core.Extensions;
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
        public TransactionTests(HttpIntegrationTestFixture fixture)
        {
            Client = fixture.FhirClient;
        }

        protected FhirClient Client { get; set; }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.CosmosDb)]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAProperBundle_WhenSubmittingATransactionForCosmosDbDataStore_ThenNotSupportedIsReturned()
        {
            FhirException ex = await Assert.ThrowsAsync<FhirException>(() => Client.PostBundleAsync(Samples.GetDefaultTransaction().ToPoco<Bundle>()));
            Assert.Equal(HttpStatusCode.MethodNotAllowed, ex.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAProperBundle_WhenSubmittingATransaction_ThenSuccessIsReturnedWithExpectedStatusCodesPerRequests()
        {
            var requestBundle = Samples.GetJsonSample("Bundle-TransactionWithValidBundleEntry");

            FhirResponse<Bundle> fhirResponse = await Client.PostBundleAsync(requestBundle.ToPoco<Bundle>());
            Assert.NotNull(fhirResponse);
            Assert.Equal(HttpStatusCode.OK, fhirResponse.StatusCode);
            ValidateResourceOutput(fhirResponse);

            var id = fhirResponse.Resource.Entry.First().Resource.Id;

            var requestResource = Samples.GetJsonSample("Bundle-TransactionWithAllValidRoutes");

            var rrequestBundle1 = requestResource.ToPoco<Bundle>();

            rrequestBundle1.Entry.Add(new EntryComponent
            {
                Request = new RequestComponent
                {
                    Method = HTTPVerb.DELETE,
                    Url = "Patient/" + id,
                },
            });

            FhirResponse<Bundle> fhirResponse1 = await Client.PostBundleAsync(rrequestBundle1);
            Assert.NotNull(fhirResponse1);
            Assert.Equal(HttpStatusCode.OK, fhirResponse1.StatusCode);
            ValidateResourceOutputForAllRoutes(fhirResponse);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenABundleWithInvalidRoutes_WhenSubmittingATransaction_ThenBadRequestExceptionIsReturnedWithProperOperationOutCome()
        {
            var requestBundle = Samples.GetJsonSample("Bundle-TransactionWithInValidRoutes");

            var fhirException = await Assert.ThrowsAsync<FhirException>(async () => await Client.PostBundleAsync(requestBundle.ToPoco<Bundle>()));
            Assert.Equal(HttpStatusCode.BadRequest, fhirException.StatusCode);

            var operationOutcome = fhirException.OperationOutcome;
            Assert.NotNull(operationOutcome.Id);
            Assert.NotEmpty(operationOutcome.Issue);
            Assert.Equal(IssueType.Invalid, operationOutcome.Issue[0].Code);
            Assert.Equal(IssueSeverity.Error, operationOutcome.Issue[0].Severity);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAProperTransactionBundle_WhenTransactionExecutionFails_ThenTransactionIsRolledBackAndProperOperationOutComeIsReturned()
        {
            var requestBundle = Samples.GetJsonSample("Bundle-TransactionForRollBack");

            var fhirException = await Assert.ThrowsAsync<FhirException>(async () => await Client.PostBundleAsync(requestBundle.ToPoco<Bundle>()));
            Assert.Equal(HttpStatusCode.NotFound, fhirException.StatusCode);

            var operationOutcome = fhirException.OperationOutcome;
            Assert.NotNull(operationOutcome.Id);
            Assert.NotEmpty(operationOutcome.Issue);

            Assert.Equal(OperationOutcome.IssueType.Processing, operationOutcome.Issue[0].Code);
            Assert.Equal(OperationOutcome.IssueSeverity.Error, operationOutcome.Issue[0].Severity);
            Assert.Equal("Transaction failed on 'GET' for the requested url '/Patient/12345'.", operationOutcome.Issue[0].Diagnostics);

            Assert.Equal(OperationOutcome.IssueType.NotFound, operationOutcome.Issue[1].Code);
            Assert.Equal(OperationOutcome.IssueSeverity.Error, operationOutcome.Issue[1].Severity);
            Assert.Equal("Resource type 'Patient' with id '12345' couldn't be found.", operationOutcome.Issue[1].Diagnostics);

            // Validate that transaction has rolledback
            Bundle bundle = await Client.SearchAsync(ResourceType.Patient, "family=ADHI");

            Assert.Empty(bundle.Entry);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenABundleWithMutipleEntriesReferringToSameResource_WhenSubmittingATransaction_ThenProperOperationOutComeIsReturned()
        {
            var requestBundle = Samples.GetJsonSample("Bundle-TransactionWithConditionalReferenceReferringToSameResource");

            var fhirException = await Assert.ThrowsAsync<FhirException>(async () => await Client.PostBundleAsync(requestBundle.ToPoco<Bundle>()));
            Assert.Equal(HttpStatusCode.BadRequest, fhirException.StatusCode);

            var operationOutcome = fhirException.OperationOutcome;
            Assert.NotNull(operationOutcome.Id);
            Assert.NotEmpty(operationOutcome.Issue);
            Assert.Equal(OperationOutcome.IssueType.Invalid, operationOutcome.Issue[0].Code);
            Assert.Equal(OperationOutcome.IssueSeverity.Error, operationOutcome.Issue[0].Severity);
            Assert.Equal("Bundle contains multiple entries that refers to the same resource 'Patient?identifier=http:/example.org/fhir/ids|234259'.", operationOutcome.Issue[0].Diagnostics);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAValidBundleWithUnauthorizedUser_WhenSubmittingATransaction_ThenOperationOutcomeWithUnAuthorizedStatusIsReturned()
        {
            FhirClient tempClient = Client.CreateClientForClientApplication(TestApplications.WrongAudienceClient);
            var requestBundle = Samples.GetJsonSample("Bundle-TransactionWithValidBundleEntry");

            var fhirException = await Assert.ThrowsAsync<FhirException>(async () => await tempClient.PostBundleAsync(requestBundle.ToPoco<Bundle>()));
            Assert.Equal(HttpStatusCode.Unauthorized, fhirException.StatusCode);

            var operationOutcome = fhirException.OperationOutcome;
            Assert.NotNull(operationOutcome.Id);
            Assert.NotEmpty(operationOutcome.Issue);
            Assert.Equal(OperationOutcome.IssueType.Login, operationOutcome.Issue[0].Code);
            Assert.Equal(OperationOutcome.IssueSeverity.Error, operationOutcome.Issue[0].Severity);
            Assert.Equal("Authentication failed.", operationOutcome.Issue[0].Diagnostics);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAValidBundleWithForbiddenUser_WhenSubmittingATransaction_ThenOperationOutcomeWithForbiddenStatusIsReturned()
        {
            FhirClient tempClient = Client.CreateClientForUser(TestUsers.ReadOnlyUser, TestApplications.NativeClient);
            var requestBundle = Samples.GetJsonSample("Bundle-TransactionWithValidBundleEntry");

            var fhirException = await Assert.ThrowsAsync<FhirException>(async () => await tempClient.PostBundleAsync(requestBundle.ToPoco<Bundle>()));
            Assert.Equal(HttpStatusCode.Forbidden, fhirException.StatusCode);

            var operationOutcome = fhirException.OperationOutcome;
            Assert.NotNull(operationOutcome.Id);
            Assert.NotEmpty(operationOutcome.Issue);
            Assert.Equal(OperationOutcome.IssueType.Processing, operationOutcome.Issue[0].Code);
            Assert.Equal(OperationOutcome.IssueSeverity.Error, operationOutcome.Issue[0].Severity);
            Assert.Equal("Transaction failed on 'POST' for the requested url '/Patient'.", operationOutcome.Issue[0].Diagnostics);

            Assert.Equal(OperationOutcome.IssueType.Forbidden, operationOutcome.Issue[1].Code);
            Assert.Equal(OperationOutcome.IssueSeverity.Error, operationOutcome.Issue[1].Severity);
            Assert.Equal("Authorization failed.", operationOutcome.Issue[1].Diagnostics);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenABundleWithInvalidConditionalReferenceInResourceBody_WhenSubmittingATransaction_ThenProperOperationOutComeIsReturned()
        {
            var observation = new Observation
            {
                Subject = new ResourceReference
                {
                    Reference = "Patient?identifier=http:/example.org/fhir/ids|234235",
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

            var fhirException = await Assert.ThrowsAsync<FhirException>(async () => await Client.PostBundleAsync(bundle));

            Assert.Equal(HttpStatusCode.BadRequest, fhirException.StatusCode);

            var operationOutcome = fhirException.OperationOutcome;
            Assert.NotNull(operationOutcome.Id);
            Assert.NotEmpty(operationOutcome.Issue);
            Assert.Equal(OperationOutcome.IssueType.Invalid, operationOutcome.Issue[0].Code);
            Assert.Equal(OperationOutcome.IssueSeverity.Error, operationOutcome.Issue[0].Severity);
            Assert.Equal("Given conditional reference 'Patient?identifier=http:/example.org/fhir/ids|234235' does not resolve to a resource.", operationOutcome.Issue[0].Diagnostics);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenABundleWithForeignReferenceInResourceBody_WhenSubmittingATransaction_ThenReferenceShouldNotBeResolvedAndProcessShouldSucceed()
        {
            var requestBundle = Samples.GetJsonSample("Bundle-TransactionWithForeignReferenceInResourceBody");

            var fhirResponse = await Client.PostBundleAsync(requestBundle.ToPoco<Bundle>());

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
        public async Task GivenATransactionBundleReferencesInResourceBody_ReferencesAreResolvedCorrectlyAsync()
        {
            var requestBundle = Samples.GetJsonSample("Bundle-TransactionWithValidBundleEntry");

            FhirResponse<Bundle> fhirResponse = await Client.PostBundleAsync(requestBundle.ToPoco<Bundle>());
            Assert.NotNull(fhirResponse);
            Assert.Equal(HttpStatusCode.OK, fhirResponse.StatusCode);

            var bundleWithConditionalReference = Samples.GetJsonSample("Bundle-TransactionWithReferenceInResourceBody");

            var bundle = bundleWithConditionalReference.ToPoco<Bundle>();

            FhirResponse<Bundle> fhirResponseForReferenceResolution = await Client.PostBundleAsync(bundle);

            Assert.NotNull(fhirResponseForReferenceResolution);
            Assert.Equal(HttpStatusCode.OK, fhirResponseForReferenceResolution.StatusCode);

            foreach (var entry in fhirResponseForReferenceResolution.Resource.Entry)
            {
                IEnumerable<ResourceReference> references = entry.Resource.GetAllChildren<ResourceReference>();

                foreach (var reference in references)
                {
                    // Asserting the conditional reference value before resolution
                    Assert.True(reference.Reference.Contains("/", System.StringComparison.Ordinal));
                }
            }
        }

        private void ValidateResourceOutput(Bundle resource)
        {
            Assert.True("201".Equals(resource.Entry[0].Response.Status), "Create");
            Assert.True("201".Equals(resource.Entry[1].Response.Status) || "200".Equals(resource.Entry[1].Response.Status), "Conditional Create");
            Assert.True("200".Equals(resource.Entry[2].Response.Status) || "201".Equals(resource.Entry[2].Response.Status), "Update");
            Assert.True("201".Equals(resource.Entry[3].Response.Status) || "200".Equals(resource.Entry[3].Response.Status), "Conditional Update");
            Assert.True("200".Equals(resource.Entry[3].Response.Status) || "200".Equals(resource.Entry[3].Response.Status), "Get");
        }

        private void ValidateResourceOutputForAllRoutes(Bundle resource)
        {
            Assert.True("201".Equals(resource.Entry[1].Response.Status) || "200".Equals(resource.Entry[1].Response.Status), "Conditional Create");
            Assert.True("200".Equals(resource.Entry[3].Response.Status), "Get");
            Assert.True("200".Equals(resource.Entry[3].Response.Status), "Get");
            Assert.True("200".Equals(resource.Entry[3].Response.Status), "Delete");
        }

        private void ValidateOperationOutcome(string actualStatusCode, OperationOutcome operationOutcome, string expectedStatusCode, string expectedDiagnostics, IssueType expectedIssueType)
        {
            Assert.Equal(expectedStatusCode, actualStatusCode);
            Assert.NotNull(operationOutcome);
            Assert.Single(operationOutcome.Issue);

            var issue = operationOutcome.Issue.First();

            Assert.Equal(IssueSeverity.Error, issue.Severity.Value);
            Assert.Equal(expectedIssueType, issue.Code);
            Assert.Equal(expectedDiagnostics, issue.Diagnostics);
        }
    }
}
