// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Audit
{
    /// <summary>
    /// Provides Audit specific tests.
    /// </summary
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Audit)]
    [HttpIntegrationFixtureArgumentSets(DataStore.CosmosDb, Format.Json)]
    public class AuditTests : IClassFixture<AuditTestFixture>
    {
        private const string RequestIdHeaderName = "X-Request-Id";
        private const string ExpectedClaimKey = "client_id";

        private readonly AuditTestFixture _fixture;
        private readonly TestFhirClient _client;
        private readonly TraceAuditLogger _auditLogger;

        public AuditTests(AuditTestFixture fixture)
        {
            _fixture = fixture;
            _client = fixture.TestFhirClient;
            _auditLogger = _fixture.AuditLogger;
        }

        [SkippableFact]
        public async Task GivenMetadata_WhenRead_ThenAuditLogEntriesShouldNotBeCreated()
        {
            // This test only works with the in-proc server with customized middleware pipeline
            Skip.If(!_fixture.IsUsingInProcTestServer);

            using FhirResponse response = await _client.ReadAsync<CapabilityStatement>("metadata");

            string correlationId = response.Headers.GetValues(RequestIdHeaderName).FirstOrDefault();

            Assert.NotNull(correlationId);

            Assert.Empty(_auditLogger.GetAuditEntriesByCorrelationId(correlationId));
        }

        [SkippableFact]
        public async Task GivenVersions_WhenRead_ThenAuditLogEntriesShouldNotBeCreated()
        {
            // This test only works with the in-proc server with customized middleware pipeline
            Skip.If(!_fixture.IsUsingInProcTestServer);

            using FhirResponse response = await _client.ReadAsync<Parameters>("$versions");

            string correlationId = response.Headers.GetValues(RequestIdHeaderName).FirstOrDefault();

            Assert.NotNull(correlationId);

            Assert.Empty(_auditLogger.GetAuditEntriesByCorrelationId(correlationId));
        }

        [SkippableFact]
        public async Task GivenAResource_WhenCreated_ThenAuditLogEntriesShouldBeCreated()
        {
            await ExecuteAndValidate(
                () => _client.CreateAsync(Samples.GetDefaultObservation().ToPoco()),
                "create",
                ResourceType.Observation,
                _ => "Observation",
                HttpStatusCode.Created);
        }

        [SkippableFact]
        public async Task GivenAnExistingResource_WhenRead_ThenAuditLogEntriesShouldBeCreated()
        {
            await ExecuteAndValidate(
                async () =>
                {
                    using FhirResponse<Patient> response = await _client.CreateAsync(Samples.GetDefaultPatient().ToPoco<Patient>());

                    return await _client.ReadAsync<Patient>(ResourceType.Patient, response.Resource.Id);
                },
                "read",
                ResourceType.Patient,
                p => $"Patient/{p.Id}",
                HttpStatusCode.OK);
        }

        [SkippableFact]
        public async Task GivenANonExistingResource_WhenRead_ThenAuditLogEntriesShouldBeCreated()
        {
            // TODO: The resource type being logged here is incorrect. The issue is tracked by https://github.com/Microsoft/fhir-server/issues/334.

            string resourceId = Guid.NewGuid().ToString();
            await ExecuteAndValidate(
                async () =>
                {
                    FhirResponse<OperationOutcome> result = null;

                    try
                    {
                        await _client.ReadAsync<Patient>(ResourceType.Patient, resourceId);
                    }
                    catch (FhirClientException ex)
                    {
                        result = ex.Response;
                        ex.Dispose();
                    }

                    // The request should have failed.
                    Assert.NotNull(result);

                    return result;
                },
                "read",
                ResourceType.Patient,
                _ => $"Patient/{resourceId}",
                HttpStatusCode.NotFound);
        }

        [SkippableFact]
        public async Task GivenAnExistingResource_WhenReadAVersion_ThenAuditLogEntriesShouldBeCreated()
        {
            await ExecuteAndValidate(
                async () =>
                {
                    using FhirResponse<Organization> result = await _client.CreateAsync(Samples.GetDefaultOrganization().ToPoco<Organization>());

                    return await _client.VReadAsync<Organization>(ResourceType.Organization, result.Resource.Id, result.Resource.Meta.VersionId);
                },
                "vread",
                ResourceType.Organization,
                o => $"Organization/{o.Id}/_history/{o.Meta.VersionId}",
                HttpStatusCode.OK);
        }

        [SkippableFact]
        public async Task GivenAnExistingResource_WhenUpdated_ThenAuditLogEntriesShouldBeCreated()
        {
            await ExecuteAndValidate(
                async () =>
                {
                    using FhirResponse<Patient> result = await _client.CreateAsync(Samples.GetDefaultPatient().ToPoco<Patient>());

                    result.Resource.Name.Add(new HumanName() { Family = "Anderson" });

                    return await _client.UpdateAsync<Patient>(result);
                },
                "update",
                ResourceType.Patient,
                p => $"Patient/{p.Id}",
                HttpStatusCode.OK);
        }

        [SkippableFact]
        public async Task GivenAnExistingResource_WhenDeleted_ThenAuditLogEntriesShouldBeCreated()
        {
            // This test only works with the in-proc server with customized middleware pipeline
            Skip.If(!_fixture.IsUsingInProcTestServer);

            using FhirResponse<Patient> result = await _client.CreateAsync(Samples.GetDefaultPatient().ToPoco<Patient>());

            using FhirResponse deleteResult = await _client.DeleteAsync(result.Resource);

            string correlationId = deleteResult.Headers.GetValues(RequestIdHeaderName).First();

            var expectedUri = new Uri($"http://localhost/Patient/{result.Resource.Id}");

            string expectedAppId = TestApplications.GlobalAdminServicePrincipal.ClientId;

            Assert.Collection(
                _auditLogger.GetAuditEntriesByCorrelationId(correlationId),
                ae => ValidateExecutingAuditEntry(ae, "delete", ResourceType.Patient, expectedUri, correlationId, expectedAppId, ExpectedClaimKey),
                ae => ValidateExecutedAuditEntry(ae, "delete", ResourceType.Patient, expectedUri, HttpStatusCode.NoContent, correlationId, expectedAppId, ExpectedClaimKey));
        }

        [SkippableFact]
        public async Task GivenAServer_WhenSearchedByResourceHistory_ThenAuditLogEntriesShouldBeCreated()
        {
            const string url = "Observation/_history";

            await ExecuteAndValidate(
                () => _client.SearchAsync(url),
                "history-type",
                ResourceType.Observation,
                _ => url,
                HttpStatusCode.OK);
        }

        [SkippableFact]
        public async Task GivenAServer_WhenSearchedByHistory_ThenAuditLogEntriesShouldBeCreated()
        {
            const string url = "_history";

            await ExecuteAndValidate(
                () => _client.SearchAsync(url),
                "history-system",
                null,
                _ => url,
                HttpStatusCode.OK);
        }

        [SkippableFact]
        public async Task GivenAServer_WhenSearchedByResourceInstance_ThenAuditLogEntriesShouldBeCreated()
        {
            await ExecuteAndValidate(
                async () =>
                {
                    using FhirResponse<Observation> result = await _client.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>());

                    return await _client.SearchAsync($"Observation/{result.Resource.Id}/_history");
                },
                "history-instance",
                ResourceType.Observation,
                b => $"Observation/{b.Entry.First().Resource.Id}/_history",
                HttpStatusCode.OK);
        }

        [SkippableFact]
        public async Task GivenAServer_WhenSearchedByCompartment_ThenAuditLogEntriesShouldBeCreated()
        {
            const string url = "Patient/123/Condition";

            await ExecuteAndValidate(
                () => _client.SearchAsync(url),
                "search",
                ResourceType.Condition,
                _ => url,
                HttpStatusCode.OK);
        }

        [SkippableFact]
        public async Task GivenAServer_WhenSearchedByResourceType_ThenAuditLogEntriesShouldBeCreated()
        {
            const string url = "Observation?_tag=123";

            await ExecuteAndValidate(
                () => _client.SearchAsync(url),
                "search-type",
                ResourceType.Observation,
                _ => url,
                HttpStatusCode.OK);
        }

        [SkippableFact]
        public async Task GivenAServer_WhenSearchedByResourceTypeUsingPost_ThenAuditLogEntriesShouldBeCreated()
        {
            await ExecuteAndValidate(
                () => _client.SearchPostAsync("Observation", default, ("_tag", "123")),
                "search-type",
                ResourceType.Observation,
                _ => "Observation/_search",
                HttpStatusCode.OK);
        }

        [SkippableFact]
        public async Task GivenAServer_WhenSearched_ThenAuditLogEntriesShouldBeCreated()
        {
            const string url = "?_tag=123";

            await ExecuteAndValidate(
                () => _client.SearchAsync(url),
                "search-system",
                null,
                _ => url,
                HttpStatusCode.OK);
        }

        [SkippableFact]
        public async Task GivenAServer_WhenSearchedUsingPost_ThenAuditLogEntriesShouldBeCreated()
        {
            await ExecuteAndValidate(
                () => _client.SearchPostAsync(null, default, ("_tag", "123")),
                "search-system",
                null,
                _ => "_search",
                HttpStatusCode.OK);
        }

        [SkippableFact]
        public async Task GivenARequest_WhenNoAuthorizationTokenIsSupplied_ThenAuditLogEntriesShouldBeCreated()
        {
            await ExecuteAndValidate(
                () =>
                {
                    var testHandler = new TestAuthenticationHttpMessageHandler(null)
                    {
                        InnerHandler = _fixture.TestFhirServer.CreateMessageHandler(),
                    };
                    return _client.Clone(testHandler);
                },
                HttpStatusCode.Unauthorized,
                expectedAppId: null);
        }

        [SkippableFact]
        public async Task GivenARequest_WhenInvalidAuthorizationTokenIsSupplied_ThenAuditLogEntriesShouldBeCreated()
        {
            await ExecuteAndValidate(
                () =>
                {
                    var testHandler = new TestAuthenticationHttpMessageHandler(new AuthenticationHeaderValue("Bearer", "invalid"))
                    {
                        InnerHandler = _fixture.TestFhirServer.CreateMessageHandler(),
                    };
                    return _client.Clone(testHandler);
                },
                HttpStatusCode.Unauthorized,
                expectedAppId: null);
        }

        [SkippableFact]
        public async Task GivenARequest_WhenValidAuthorizationTokenWithInvalidAudienceIsSupplied_ThenAuditLogEntriesShouldBeCreated()
        {
            await ExecuteAndValidate(
                () => _client.CreateClientForClientApplication(TestApplications.WrongAudienceClient),
                HttpStatusCode.Unauthorized,
                expectedAppId: null);
        }

        [SkippableFact]
        public async Task GivenASmartOnFhirRequest_WhenAuthorizeIsCalled_TheAuditLogEntriesShouldBeCreated()
        {
            const string pathSegment = "AadSmartOnFhirProxy/authorize?client_id=1234&response_type=json&redirect_uri=httptest&aud=localhost";
            await ExecuteAndValidate(
                async () => await _client.HttpClient.GetAsync(pathSegment),
                "smart-on-fhir-authorize",
                pathSegment,
                HttpStatusCode.Redirect,
                "1234",
                "client_id");
        }

        [SkippableFact]
        public async Task GivenASmartOnFhirRequest_WhenCallbackIsCalled_TheAuditLogEntriesShouldBeCreated()
        {
            const string pathSegment = "AadSmartOnFhirProxy/callback/aHR0cHM6Ly9sb2NhbGhvc3Q=?code=1234&state=1234&session_state=1234";
            await ExecuteAndValidate(
                async () => await _client.HttpClient.GetAsync(pathSegment),
                "smart-on-fhir-callback",
                pathSegment,
                HttpStatusCode.BadRequest,
                null,
                null);
        }

        [SkippableFact]
        public async Task GivenASmartOnFhirRequest_WhenTokenIsCalled_TheAuditLogEntriesShouldBeCreated()
        {
            const string pathSegment = "AadSmartOnFhirProxy/token";
            var formFields = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("client_id", "1234"),
                new KeyValuePair<string, string>("grant_type", "grantType"),
                new KeyValuePair<string, string>("code", "code"),
                new KeyValuePair<string, string>("redirect_uri", "redirectUri"),
                new KeyValuePair<string, string>("client_secret", "client_secret"),
            };

            var content = new FormUrlEncodedContent(formFields);
            content.Headers.Add(KnownHeaders.CustomAuditHeaderPrefix + "test", "test");
            await ExecuteAndValidate(
                async () => await _client.HttpClient.PostAsync(pathSegment, content),
                "smart-on-fhir-token",
                pathSegment,
                HttpStatusCode.BadRequest,
                "1234",
                "client_id",
                new Dictionary<string, string>() { [KnownHeaders.CustomAuditHeaderPrefix + "test"] = "test" });
        }

        [SkippableFact]
        public async Task GivenAResource_WhenNotAuthorized_ThenAuditLogEntriesShouldBeCreated()
        {
            await ExecuteAndValidate(
                () => _client.CreateClientForClientApplication(TestApplications.NativeClient),
                HttpStatusCode.Forbidden,
                expectedAppId: TestApplications.NativeClient.ClientId);
        }

        [HttpIntegrationFixtureArgumentSets(DataStore.All)]
        [SkippableFact]
        [Trait(Traits.Priority, Priority.One)]
        [Trait(Traits.Category, Categories.Batch)]
        public async Task GivenABatch_WhenPost_ThenAuditLogEntriesShouldBeCreated()
        {
            var batch = Samples.GetDefaultBatch().ToPoco<Bundle>();

            await _client.UpdateAsync(batch.Entry[2].Resource as Patient);

            List<(string expectedActions, string expectedPathSegments, HttpStatusCode? expectedStatusCodes, ResourceType? resourceType)> expectedList = new List<(string, string, HttpStatusCode?, ResourceType?)>
            {
                ("batch", string.Empty, HttpStatusCode.OK, null),
                ("delete", batch.Entry[5].Request.Url, HttpStatusCode.NoContent, ResourceType.Patient),
                ("conditional-delete", batch.Entry[6].Request.Url, HttpStatusCode.NoContent, ResourceType.Patient),
                ("create", batch.Entry[0].Request.Url, HttpStatusCode.Created, ResourceType.Patient),
                ("conditional-create", batch.Entry[1].Request.Url, HttpStatusCode.Created, ResourceType.Patient),
                ("update", batch.Entry[2].Request.Url, HttpStatusCode.OK, ResourceType.Patient),
                ("conditional-update", batch.Entry[3].Request.Url, HttpStatusCode.Created, ResourceType.Patient),
                ("update", batch.Entry[4].Request.Url, Constants.IfMatchFailureStatus, ResourceType.Patient),
                ("search-type", batch.Entry[8].Request.Url, HttpStatusCode.OK, ResourceType.Patient),
                ("read", batch.Entry[9].Request.Url, HttpStatusCode.NotFound, ResourceType.Patient),
            };

            await ExecuteAndValidateBundle(
               () =>
               {
                   return _client.PostBundleAsync(batch);
               },
               expectedList,
               TestApplications.GlobalAdminServicePrincipal.ClientId);
        }

        [HttpIntegrationFixtureArgumentSets(DataStore.All)]
        [SkippableFact]
        [Trait(Traits.Priority, Priority.One)]
        [Trait(Traits.Category, Categories.Authorization)]
        [Trait(Traits.Category, Categories.Batch)]
        public async Task GivenABatchAndUserWithoutWrite_WhenPost_ThenAuditLogEntriesShouldBeCreated()
        {
            var batch = new Bundle
            {
                Type = Bundle.BundleType.Batch,
                Entry = new List<Bundle.EntryComponent>
                {
                    new Bundle.EntryComponent
                    {
                        Resource = Samples.GetDefaultObservation().ToPoco(),
                        Request = new Bundle.RequestComponent
                        {
                            Method = Bundle.HTTPVerb.POST,
                            Url = "Observation",
                        },
                    },
                    new Bundle.EntryComponent
                    {
                        Request = new Bundle.RequestComponent
                        {
                            Method = Bundle.HTTPVerb.GET,
                            Url = "Patient?name=peter",
                        },
                    },
                },
            };

            List<(string expectedActions, string expectedPathSegments, HttpStatusCode? expectedStatusCodes, ResourceType? resourceType)> expectedList = new List<(string, string, HttpStatusCode?, ResourceType?)>
            {
                ("batch", string.Empty, HttpStatusCode.OK, null),
                ("create", "Observation", HttpStatusCode.Forbidden, ResourceType.Observation),
                ("search-type", "Patient?name=peter", HttpStatusCode.OK, ResourceType.Patient),
            };

            TestFhirClient tempClient = _client.CreateClientForUser(TestUsers.ReadOnlyUser, TestApplications.NativeClient);

            await ExecuteAndValidateBundle(
                () => tempClient.PostBundleAsync(batch),
                expectedList,
                TestApplications.NativeClient.ClientId);
        }

        private async Task ExecuteAndValidateBundle<T>(Func<Task<FhirResponse<T>>> action, List<(string auditAction, string route, HttpStatusCode? statusCode, ResourceType? resourceType)> expectedList, string expectedAppId)
            where T : Resource
        {
            // This test only works with the in-proc server with customized middleware pipeline
            Skip.If(!_fixture.IsUsingInProcTestServer);

            using FhirResponse<T> response = await action();

            string correlationId = response.Headers.GetValues(RequestIdHeaderName).FirstOrDefault();
            Assert.NotNull(correlationId);

            var inspectors = new List<Action<AuditEntry>>();

            int insertIndex = 0;
            foreach ((string auditAction, string route, HttpStatusCode? statusCode, ResourceType? resourceType) in expectedList)
            {
                if (insertIndex == 2 && inspectors.Count == 2)
                {
                    insertIndex--;
                }

                if (statusCode != HttpStatusCode.Unauthorized)
                {
                    inspectors.Insert(insertIndex, ae => ValidateExecutingAuditEntry(ae, auditAction, resourceType, new Uri($"http://localhost/{route}"), correlationId, expectedAppId, ExpectedClaimKey));
                    insertIndex++;
                }

                inspectors.Insert(insertIndex, ae => ValidateExecutedAuditEntry(ae, auditAction, resourceType, new Uri($"http://localhost/{route}"), statusCode, correlationId, expectedAppId, ExpectedClaimKey));
                insertIndex++;
            }

            Assert.Collection(
                _auditLogger.GetAuditEntriesByCorrelationId(correlationId),
                inspectors.ToArray());
        }

        [SkippableFact]
        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.SqlServer)]
        [Trait(Traits.Category, Categories.Transaction)]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenATransactionBundleWithValidEntries_WhenSuccessfulPost_ThenAuditLogEntriesShouldBeCreated()
        {
            var requestBundle = Samples.GetTransactionBundleWithValidEntries();
            var batch = requestBundle.ToPoco<Bundle>();

            await _client.UpdateAsync<Patient>(batch.Entry[2].Resource as Patient);

            // Even entries are audit executed entry and odd entries are audit executing entry
            List<(string expectedActions, string expectedPathSegments, HttpStatusCode? expectedStatusCodes, ResourceType? resourceType)> expectedList = new List<(string, string, HttpStatusCode?, ResourceType?)>
            {
                ("transaction", string.Empty, HttpStatusCode.OK, null),
                ("create", batch.Entry[0].Request.Url, HttpStatusCode.Created, ResourceType.Patient),
                ("conditional-create", batch.Entry[1].Request.Url, HttpStatusCode.Created, ResourceType.Patient),
                ("update", batch.Entry[2].Request.Url, HttpStatusCode.OK, ResourceType.Patient),
                ("conditional-update", batch.Entry[3].Request.Url, HttpStatusCode.Created, ResourceType.Patient),
            };

            await ExecuteAndValidateBundle(
               () => _client.PostBundleAsync(requestBundle.ToPoco<Hl7.Fhir.Model.Bundle>()),
               expectedList,
               TestApplications.GlobalAdminServicePrincipal.ClientId);
        }

        [SkippableFact]
        [HttpIntegrationFixtureArgumentSets(dataStores: DataStore.SqlServer)]
        [Trait(Traits.Category, Categories.Transaction)]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenATransactionBundle_WhenAnUnsuccessfulPost_ThenTransactionShouldRollBackAndAuditLogEntriesShouldBeCreated()
        {
            List<(string expectedActions, string expectedPathSegments, HttpStatusCode? expectedStatusCodes, ResourceType? resourceType)> expectedList = new List<(string, string, HttpStatusCode?, ResourceType?)>
            {
                ("transaction", string.Empty, HttpStatusCode.NotFound, null),
                ("create", "Patient", HttpStatusCode.Created, ResourceType.Patient),
                ("read", "Patient/12345", HttpStatusCode.NotFound, ResourceType.Patient),
            };

            var requestBundle = Samples.GetJsonSample("Bundle-TransactionForRollBack");

            await ExecuteAndValidateBundle(
              async () =>
              {
                  using var fhirException = await Assert.ThrowsAsync<FhirClientException>(async () => await _client.PostBundleAsync(requestBundle.ToPoco<Bundle>()));

                  return fhirException.Response;
              },
              expectedList,
              TestApplications.GlobalAdminServicePrincipal.ClientId);
        }

        private async Task ExecuteAndValidate<T>(Func<Task<FhirResponse<T>>> action, string expectedAction, ResourceType? expectedResourceType, Func<T, string> expectedPathGenerator, HttpStatusCode expectedStatusCode)
            where T : Resource
        {
            // This test only works with the in-proc server with customized middleware pipeline
            Skip.If(!_fixture.IsUsingInProcTestServer);

            using FhirResponse<T> response = await action();

            string correlationId = response.Headers.GetValues(RequestIdHeaderName).FirstOrDefault();

            Assert.NotNull(correlationId);

            var expectedUri = new Uri($"http://localhost/{expectedPathGenerator(response.Resource)}");

            string expectedAppId = TestApplications.GlobalAdminServicePrincipal.ClientId;

            Assert.Collection(
                _auditLogger.GetAuditEntriesByCorrelationId(correlationId),
                ae => ValidateExecutingAuditEntry(ae, expectedAction, expectedResourceType, expectedUri, correlationId, expectedAppId, ExpectedClaimKey),
                ae => ValidateExecutedAuditEntry(ae, expectedAction, expectedResourceType, expectedUri, expectedStatusCode, correlationId, expectedAppId, ExpectedClaimKey));
        }

        private async Task ExecuteAndValidate(Func<Task<HttpResponseMessage>> action, string expectedAction, string expectedPathSegment, HttpStatusCode expectedStatusCode, string expectedClaimValue, string expectedClaimKey, Dictionary<string, string> expectedCustomAuditHeaders = null)
        {
            // This test only works with the in-proc server with customized middleware pipeline
            Skip.If(!_fixture.IsUsingInProcTestServer);

            HttpResponseMessage response = await action();

            string correlationId = response.Headers.GetValues(RequestIdHeaderName).FirstOrDefault();

            Assert.NotNull(correlationId);

            var expectedUri = new Uri($"http://localhost/{expectedPathSegment}");

            Assert.Collection(
                _auditLogger.GetAuditEntriesByCorrelationId(correlationId),
                ae => ValidateExecutingAuditEntry(ae, expectedAction, null, expectedUri, correlationId, expectedClaimValue, expectedClaimKey, expectedCustomAuditHeaders),
                ae => ValidateExecutedAuditEntry(ae, expectedAction, null, expectedUri, expectedStatusCode, correlationId, expectedClaimValue, expectedClaimKey, expectedCustomAuditHeaders));
        }

        private async Task ExecuteAndValidate(Func<TestFhirClient> createClient, HttpStatusCode expectedStatusCode, string expectedAppId)
        {
            // This test only works with the in-proc server with customized middleware pipeline
            Skip.If(!_fixture.IsUsingInProcTestServer);

            const string url = "Patient/123";

            // Create a new client with no token supplied.
            var client = createClient();

            using FhirResponse<OperationOutcome> response = (await Assert.ThrowsAsync<FhirClientException>(() => client.ReadAsync<Patient>(url))).Response;

            string correlationId = response.Headers.GetValues(RequestIdHeaderName).FirstOrDefault();

            Assert.NotNull(correlationId);

            var expectedUri = new Uri($"http://localhost/{url}");

            var inspectors = new List<Action<AuditEntry>>
            {
                ae => ValidateExecutedAuditEntry(ae, "read", ResourceType.Patient, expectedUri, expectedStatusCode, correlationId, expectedAppId, ExpectedClaimKey),
            };

            if (expectedStatusCode != HttpStatusCode.Unauthorized)
            {
                // we expect an executing entry as well

                inspectors.Insert(
                    0,
                    ae => ValidateExecutingAuditEntry(ae, "read", ResourceType.Patient, expectedUri, correlationId, expectedAppId, ExpectedClaimKey));
            }

            Assert.Collection(
                _auditLogger.GetAuditEntriesByCorrelationId(correlationId),
                inspectors.ToArray());
        }

        private void ValidateExecutingAuditEntry(AuditEntry auditEntry, string expectedAction, ResourceType? expectedResourceType, Uri expectedUri, string expectedCorrelationId, string expectedClaimValue, string expectedClaimKey, Dictionary<string, string> expectedCustomAuditHeaders = null)
        {
            ValidateAuditEntry(auditEntry, AuditAction.Executing, expectedAction, expectedResourceType, expectedUri, null, expectedCorrelationId, expectedClaimValue, expectedClaimKey, expectedCustomAuditHeaders);
        }

        private void ValidateExecutedAuditEntry(AuditEntry auditEntry, string expectedAction, ResourceType? expectedResourceType, Uri expectedUri, HttpStatusCode? expectedStatusCode, string expectedCorrelationId, string expectedClaimValue, string expectedClaimKey, Dictionary<string, string> expectedCustomAuditHeaders = null)
        {
            ValidateAuditEntry(auditEntry, AuditAction.Executed, expectedAction, expectedResourceType, expectedUri, expectedStatusCode, expectedCorrelationId, expectedClaimValue, expectedClaimKey, expectedCustomAuditHeaders);
        }

        private void ValidateAuditEntry(AuditEntry auditEntry, AuditAction expectedAuditAction, string expectedAction, ResourceType? expectedResourceType, Uri expectedUri, HttpStatusCode? expectedStatusCode, string expectedCorrelationId, string expectedClaimValue, string expectedClaimKey, Dictionary<string, string> expectedCustomAuditHeaders = null)
        {
            Assert.NotNull(auditEntry);
            Assert.Equal(expectedAuditAction, auditEntry.AuditAction);
            Assert.Equal(expectedAction, auditEntry.Action);
            Assert.Equal(expectedResourceType?.ToString(), auditEntry.ResourceType);
            Assert.Equal(expectedUri, auditEntry.RequestUri);
            Assert.Equal(expectedStatusCode, auditEntry.StatusCode);
            Assert.Equal(expectedCorrelationId, auditEntry.CorrelationId);

            // Unfortunately, we cannot test the caller IP because these tests only run in-process, which does not go through network.

            if (expectedClaimValue != null)
            {
                Assert.Contains(
                    new KeyValuePair<string, string>(expectedClaimKey, expectedClaimValue), auditEntry.CallerClaims);
            }
            else
            {
                Assert.Empty(auditEntry.CallerClaims);
            }

            if (expectedCustomAuditHeaders != null)
            {
                Assert.Equal(expectedCustomAuditHeaders, auditEntry.CustomHeaders);
            }
            else
            {
                Assert.Empty(auditEntry.CustomHeaders);
            }
        }
    }
}
