// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Extensions;
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
    [Trait(Traits.Category, Categories.Bundle)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.All)]
    public class BatchTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private readonly TestFhirClient _client;
        private readonly Dictionary<HttpStatusCode, string> _statusCodeMap = new Dictionary<HttpStatusCode, string>()
        {
            { HttpStatusCode.NoContent, "204" },
            { HttpStatusCode.NotFound, "404" },
            { HttpStatusCode.OK, "200" },
            { HttpStatusCode.PreconditionFailed, "412" },
            { HttpStatusCode.Created, "201" },
            { HttpStatusCode.Forbidden, "403" },
        };

        public BatchTests(HttpIntegrationTestFixture fixture)
        {
            _client = fixture.TestFhirClient;
        }

        [SkippableTheory]
        [Trait(Traits.Priority, Priority.One)]
        [InlineData(FhirBundleProcessingLogic.Parallel)]
        [InlineData(FhirBundleProcessingLogic.Sequential)]
        public async Task GivenAValidBundle_WhenSubmittingABatch_ThenSuccessIsReturnedForBatchAndExpectedStatusCodesPerRequests(FhirBundleProcessingLogic processingLogic)
        {
            Skip.If(ModelInfoProvider.Version == FhirSpecification.Stu3, "Patch isn't supported in Bundles by STU3");

            CancellationTokenSource source = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var requestBundle = Samples.GetBatchWithDuplicatedItems().ToPoco<Bundle>();

            await _client.UpdateAsync(requestBundle.Entry[1].Resource as Patient, cancellationToken: source.Token);

            using FhirResponse<Bundle> fhirResponse = await _client.PostBundleAsync(requestBundle, processingLogic: processingLogic, source.Token);
            Assert.NotNull(fhirResponse);
            Assert.Equal(HttpStatusCode.OK, fhirResponse.StatusCode);

            Bundle resource = fhirResponse.Resource;

            Assert.Equal("201", resource.Entry[0].Response.Status);

            // Resources 1, 2 and 3 have the same resource Id.
            Assert.Equal("200", resource.Entry[1].Response.Status); // PUT

            if (processingLogic == FhirBundleProcessingLogic.Parallel)
            {
                // Duplicated records. Only one should successed. As the requests are processed in parallel,
                // it's not possible to pick the one that will be processed.
                if (resource.Entry[2].Response.Status == "200")
                {
                    Assert.Equal("200", resource.Entry[2].Response.Status); // PATCH
                    Assert.Equal("400", resource.Entry[3].Response.Status); // PATCH (Duplicate)
                }
                else
                {
                    Assert.Equal("400", resource.Entry[2].Response.Status); // PATCH (Duplicate)
                    Assert.Equal("200", resource.Entry[3].Response.Status); // PATCH
                }
            }
            else if (processingLogic == FhirBundleProcessingLogic.Sequential)
            {
                Assert.Equal("200", resource.Entry[2].Response.Status); // PATCH
                Assert.Equal("200", resource.Entry[3].Response.Status); // PATCH
            }

            Assert.Equal("204", resource.Entry[4].Response.Status);
            Assert.Equal("204", resource.Entry[5].Response.Status);

            ValidateOperationOutcome(resource.Entry[6].Response.Status, resource.Entry[6].Response.Outcome as OperationOutcome, _statusCodeMap[HttpStatusCode.NotFound], "The route for \"/ValueSet/$lookup\" was not found.", IssueType.NotFound);
            Assert.Equal("200", resource.Entry[7].Response.Status);
            ValidateOperationOutcome(resource.Entry[8].Response.Status, resource.Entry[8].Response.Outcome as OperationOutcome, _statusCodeMap[HttpStatusCode.NotFound], "Resource type 'Patient' with id '12334' couldn't be found.", IssueType.NotFound);
        }

        [Fact]
        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.CosmosDb)]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAValidBundle_WhenSubmittingABatchTwiceWithAndWithoutChanges_ThenVersionIsCreatedWhenDataIsChanged()
        {
            // This test has a dependency to bundle sequential processing logic.
            FhirBundleProcessingLogic processingLogic = FhirBundleProcessingLogic.Sequential;

            var requestBundle = Samples.GetDefaultBatch().ToPoco<Bundle>();
            using FhirResponse<Bundle> fhirResponse = await _client.PostBundleAsync(
                requestBundle,
                processingLogic: processingLogic);

            Assert.NotNull(fhirResponse);
            Assert.Equal(HttpStatusCode.OK, fhirResponse.StatusCode);

            Bundle resource = fhirResponse.Resource;

            Assert.Equal("201", resource.Entry[0].Response.Status);
            Assert.Equal("201", resource.Entry[1].Response.Status);
            Assert.Equal("201", resource.Entry[2].Response.Status);
            Assert.Equal("1", resource.Entry[2].Resource.VersionId);
            Assert.Equal("201", resource.Entry[3].Response.Status);
            Assert.Equal(((int)Constants.IfMatchFailureStatus).ToString(), resource.Entry[4].Response.Status);
            Assert.Equal("204", resource.Entry[5].Response.Status);
            Assert.Equal("204", resource.Entry[6].Response.Status);
            ValidateOperationOutcome(resource.Entry[7].Response.Status, resource.Entry[7].Response.Outcome as OperationOutcome, _statusCodeMap[HttpStatusCode.NotFound], "The route for \"/ValueSet/$lookup\" was not found.", IssueType.NotFound);
            Assert.Equal("200", resource.Entry[8].Response.Status);
            ValidateOperationOutcome(resource.Entry[9].Response.Status, resource.Entry[9].Response.Outcome as OperationOutcome, _statusCodeMap[HttpStatusCode.NotFound], "Resource type 'Patient' with id '12334' couldn't be found.", IssueType.NotFound);

            // WhenSubmittingABatchTwiceWithNoDataChange_ThenServerShouldNotCreateAVersionSecondTimeAndSendOk
            using FhirResponse<Bundle> fhirResponseAfterPostingSameBundle = await _client.PostBundleAsync(
                requestBundle,
                processingLogic);

            Assert.NotNull(fhirResponseAfterPostingSameBundle);
            Assert.Equal(HttpStatusCode.OK, fhirResponseAfterPostingSameBundle.StatusCode);

            Bundle resourceAfterPostingSameBundle = fhirResponseAfterPostingSameBundle.Resource;

            Assert.Equal("201", resourceAfterPostingSameBundle.Entry[0].Response.Status);
            Assert.Equal("200", resourceAfterPostingSameBundle.Entry[1].Response.Status);
            Assert.Equal("200", resourceAfterPostingSameBundle.Entry[2].Response.Status);
            Assert.Equal(resource.Entry[2].Resource.VersionId, resourceAfterPostingSameBundle.Entry[2].Resource.VersionId);
            Assert.Equal("200", resourceAfterPostingSameBundle.Entry[3].Response.Status);
            Assert.Equal(resource.Entry[3].Resource.VersionId, resourceAfterPostingSameBundle.Entry[3].Resource.VersionId);
            Assert.Equal(((int)Constants.IfMatchFailureStatus).ToString(), resourceAfterPostingSameBundle.Entry[4].Response.Status);
            Assert.Equal("204", resourceAfterPostingSameBundle.Entry[5].Response.Status);
            Assert.Equal("204", resourceAfterPostingSameBundle.Entry[6].Response.Status);
            ValidateOperationOutcome(resourceAfterPostingSameBundle.Entry[7].Response.Status, resourceAfterPostingSameBundle.Entry[7].Response.Outcome as OperationOutcome, _statusCodeMap[HttpStatusCode.NotFound], "The route for \"/ValueSet/$lookup\" was not found.", IssueType.NotFound);
            Assert.Equal("200", resourceAfterPostingSameBundle.Entry[8].Response.Status);
            Assert.Equal(resource.Entry[8].Resource.VersionId, resourceAfterPostingSameBundle.Entry[8].Resource.VersionId);
            ValidateOperationOutcome(resourceAfterPostingSameBundle.Entry[9].Response.Status, resourceAfterPostingSameBundle.Entry[9].Response.Outcome as OperationOutcome, _statusCodeMap[HttpStatusCode.NotFound], "Resource type 'Patient' with id '12334' couldn't be found.", IssueType.NotFound);
        }

        [Theory]
        [Trait(Traits.Priority, Priority.One)]
        [Trait(Traits.Category, Categories.Authorization)]
        [InlineData(FhirBundleProcessingLogic.Parallel)]
        [InlineData(FhirBundleProcessingLogic.Sequential)]
        public async Task GivenAValidBundleWithReadonlyUser_WhenSubmittingABatch_ThenForbiddenAndOutcomeIsReturned(FhirBundleProcessingLogic processingLogic)
        {
            TestFhirClient tempClient = _client.CreateClientForUser(TestUsers.ReadOnlyUser, TestApplications.NativeClient);
            Bundle requestBundle = Samples.GetDefaultBatch().ToPoco<Bundle>();

            using FhirResponse<Bundle> fhirResponse = await tempClient.PostBundleAsync(requestBundle, processingLogic);

            Assert.NotNull(fhirResponse);
            Assert.Equal(HttpStatusCode.OK, fhirResponse.StatusCode);

            for (int i = 0; i < 7; i++)
            {
                var entry = fhirResponse.Resource.Entry[i];
                var operationOutcome = entry.Response.Outcome as OperationOutcome;
                ValidateOperationOutcome(entry.Response.Status, operationOutcome, _statusCodeMap[HttpStatusCode.Forbidden], "Authorization failed.", IssueType.Forbidden);
            }

            var resourceEntry = fhirResponse.Resource.Entry[7];
            ValidateOperationOutcome(resourceEntry.Response.Status, resourceEntry.Response.Outcome as OperationOutcome, _statusCodeMap[HttpStatusCode.NotFound], "The route for \"/ValueSet/$lookup\" was not found.", IssueType.NotFound);
            resourceEntry = fhirResponse.Resource.Entry[8];
            Assert.Equal("200", resourceEntry.Response.Status);
            Assert.Null(resourceEntry.Response.Outcome);
            resourceEntry = fhirResponse.Resource.Entry[9];
            ValidateOperationOutcome(resourceEntry.Response.Status, resourceEntry.Response.Outcome as OperationOutcome, _statusCodeMap[HttpStatusCode.NotFound], "Resource type 'Patient' with id '12334' couldn't be found.", IssueType.NotFound);
        }

        [Theory]
        [Trait(Traits.Priority, Priority.One)]
        [InlineData(FhirBundleProcessingLogic.Parallel)]
        [InlineData(FhirBundleProcessingLogic.Sequential)]
        public async Task GivenANonBundleResource_WhenSubmittingABatch_ThenBadRequestIsReturned(FhirBundleProcessingLogic processingLogic)
        {
            using FhirClientException ex = await Assert.ThrowsAsync<FhirClientException>(() => _client.PostBundleAsync(Samples.GetDefaultObservation().ToPoco<Observation>(), processingLogic));

            Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        }

        [Theory]
        [Trait(Traits.Priority, Priority.One)]
        [InlineData(FhirBundleProcessingLogic.Parallel)]
        [InlineData(FhirBundleProcessingLogic.Sequential)]
        public async Task GivenBundleTypeIsMissing_WhenSubmittingABundle_ThenMethodNotAllowedExceptionIsReturned(FhirBundleProcessingLogic processingLogic)
        {
            using FhirClientException ex = await Assert.ThrowsAsync<FhirClientException>(() => _client.PostBundleAsync(Samples.GetJsonSample("Bundle-TypeMissing").ToPoco<Bundle>(), processingLogic));
            ValidateOperationOutcome(ex.StatusCode.ToString(), ex.OperationOutcome, "MethodNotAllowed", "Bundle type is not present. Possible values are: transaction or batch", IssueType.Forbidden);
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

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenABatchBundle_WithProfileValidationFlag_ReturnsABundleResponse(bool profileValidation)
        {
            var bundle = new Hl7.Fhir.Model.Bundle
            {
                Type = Bundle.BundleType.Batch,
                Entry = new List<EntryComponent>
                {
                    new EntryComponent
                    {
                        Request = new RequestComponent
                        {
                            Method = HTTPVerb.POST,
                            Url = "Patient",
                        },
                        Resource = Samples.GetJsonSample("Profile-Patient-uscore-noGender").ToPoco<Patient>(),
                    },
                    new EntryComponent
                    {
                        Request = new RequestComponent
                        {
                            Method = HTTPVerb.POST,
                            Url = "Patient",
                        },
                        Resource = Samples.GetJsonSample("Patient").ToPoco<Patient>(),
                    },
                    new EntryComponent
                    {
                        Request = new RequestComponent
                        {
                            Method = HTTPVerb.POST,
                            Url = "Patient",
                        },
                        Resource = Samples.GetJsonSample("Profile-Patient-uscore-noidentifier").ToPoco<Patient>(),
                    },
                },
            };

            using FhirResponse<Bundle> fhirResponse = await _client.PostBundleWithValidationHeaderAsync(bundle, profileValidation);
            Assert.NotNull(fhirResponse);
            Assert.Equal(HttpStatusCode.OK, fhirResponse.StatusCode);
            Bundle bundleResource = fhirResponse.Resource;

            if (profileValidation)
            {
                Assert.Equal("400", bundleResource.Entry[0].Response.Status);
                Assert.Equal("201", bundleResource.Entry[1].Response.Status);
                Assert.Equal("400", bundleResource.Entry[2].Response.Status);
            }
            else
            {
                Assert.Equal("201", bundleResource.Entry[0].Response.Status);
                Assert.Equal("201", bundleResource.Entry[1].Response.Status);
                Assert.Equal("201", bundleResource.Entry[2].Response.Status);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer)]
        public async Task GivenATransactionBundle_WithProfileValidationFlag_ReturnsABundleResponse(bool profileValidation)
        {
            var bundle = new Hl7.Fhir.Model.Bundle
            {
                Type = Bundle.BundleType.Transaction,
                Entry = new List<EntryComponent>
                {
                    new EntryComponent
                    {
                        Request = new RequestComponent
                        {
                            Method = HTTPVerb.POST,
                            Url = "Patient",
                        },
                        Resource = Samples.GetJsonSample("Profile-Patient-uscore-noGender").ToPoco<Patient>(),
                    },
                    new EntryComponent
                    {
                        Request = new RequestComponent
                        {
                            Method = HTTPVerb.POST,
                            Url = "Patient",
                        },
                        Resource = Samples.GetJsonSample("Patient").ToPoco<Patient>(),
                    },
                    new EntryComponent
                    {
                        Request = new RequestComponent
                        {
                            Method = HTTPVerb.POST,
                            Url = "Patient",
                        },
                        Resource = Samples.GetJsonSample("Profile-Patient-uscore-noidentifier").ToPoco<Patient>(),
                    },
                },
            };

            if (profileValidation)
            {
                using FhirClientException ex = await Assert.ThrowsAsync<FhirClientException>(() => _client.PostBundleWithValidationHeaderAsync(bundle, profileValidation));
                Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
            }
            else
            {
                using FhirResponse<Bundle> fhirResponse = await _client.PostBundleWithValidationHeaderAsync(bundle, false);
                Assert.NotNull(fhirResponse);
                Assert.Equal(HttpStatusCode.OK, fhirResponse.StatusCode);

                Bundle bundleResource = fhirResponse.Resource;
                Assert.Equal("201", bundleResource.Entry[0].Response.Status);
                Assert.Equal("201", bundleResource.Entry[1].Response.Status);
                Assert.Equal("201", bundleResource.Entry[2].Response.Status);
            }
        }
    }
}
