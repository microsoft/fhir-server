// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    /// <summary>
    /// NOTE: These tests will fail if security is disabled..
    /// </summary>
    [Trait(Traits.Category, Categories.Authorization)]
    [HttpIntegrationFixtureArgumentSets(DataStore.CosmosDb, Format.Json)]
    public class BasicAuthTests : IClassFixture<HttpIntegrationTestFixture>
    {
        private static readonly Regex WwwAuthenticatePattern = new Regex(@"authorization_uri=\""(?<authorization_uri>[^\s,]+)+\"", resource_id=\""(?<resource_id>[^\s,]+)+\"", realm=\""(?<realm>[^\s,]+)+\""", RegexOptions.IgnoreCase);

        private const string ForbiddenMessage = "Forbidden: Authorization failed.";
        private const string UnauthorizedMessage = "Unauthorized: Authentication failed.";
        private const string InvalidToken = "eyJhbGciOiJSUzI1NiIsImtpZCI6ImNmNWRmMGExNzY5ZWIzZTFkOGRiNWIxMGZiOWY3ZTk0IiwidHlwIjoiSldUIn0.eyJuYmYiOjE1NDQ2ODQ1NzEsImV4cCI6MTU0NDY4ODE3MSwiaXNzIjoiaHR0cHM6Ly9sb2NhbGhvc3Q6NDQzNDgiLCJhdWQiOlsiaHR0cHM6Ly9sb2NhbGhvc3Q6NDQzNDgvcmVzb3VyY2VzIiwiZmhpci1haSJdLCJjbGllbnRfaWQiOiJzZXJ2aWNlY2xpZW50Iiwicm9sZXMiOiJhZG1pbiIsImFwcGlkIjoic2VydmljZWNsaWVudCIsInNjb3BlIjpbImZoaXItYWkiXX0.SKSvy6Jxzwsv1ZSi0PO4Pdq6QDZ6mBJIRxUPgoPlz2JpiB6GMXu5u0n1IpS6zOXihGkGhegjtcqj-6TKE6Ou5uhQ0VTnmf-NxcYKFl48aDihcGem--qa2V8GC7na549Ctj1PLXoYUbovV4LB27Kj3X83sZVnWdHqg_G0AKo4xm7hr23VUvJ1D73lEcYaGd5K9GXHNgUrJO5v288y0uCXZ5ByNDJ-K6Xi7_68dLdshlIiHaeIBuC3rhchSf2hdglkQgOyo4g4gT_HfKjwdrrpGzepNXOPQEwtUs_o2uriXAd7FfbL_Q4ORiDWPXkmwBXqo7uUfg-2SnT3DApc3PuA0";

        private readonly TestFhirClient _client;

        public BasicAuthTests(HttpIntegrationTestFixture fixture)
        {
            _client = fixture.TestFhirClient;
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAUserWithNoCreatePermissions_WhenCreatingAResource_TheServerShouldReturnForbidden()
        {
            TestFhirClient tempClient = _client.CreateClientForUser(TestUsers.ReadOnlyUser, TestApplications.NativeClient);
            FhirException fhirException = await Assert.ThrowsAsync<FhirException>(async () => await tempClient.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>()));
            Assert.Equal(ForbiddenMessage, fhirException.Message);
            Assert.Equal(HttpStatusCode.Forbidden, fhirException.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAUserWithNoWritePermissions_WhenUpdatingAResource_TheServerShouldReturnForbidden()
        {
            TestFhirClient tempClient = _client.CreateClientForUser(TestUsers.ReadWriteUser, TestApplications.NativeClient);
            Observation createdResource = await tempClient.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>());

            tempClient = _client.CreateClientForUser(TestUsers.ReadOnlyUser, TestApplications.NativeClient);
            FhirException fhirException = await Assert.ThrowsAsync<FhirException>(async () => await tempClient.UpdateAsync(createdResource));
            Assert.Equal(ForbiddenMessage, fhirException.Message);
            Assert.Equal(HttpStatusCode.Forbidden, fhirException.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAUserWithNoHardDeletePermissions_WhenHardDeletingAResource_TheServerShouldReturnForbidden()
        {
            TestFhirClient tempClient = _client.CreateClientForUser(TestUsers.ReadWriteUser, TestApplications.NativeClient);
            Observation createdResource = await tempClient.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>());

            FhirException fhirException = await Assert.ThrowsAsync<FhirException>(async () => await tempClient.HardDeleteAsync(createdResource));
            Assert.Equal(ForbiddenMessage, fhirException.Message);
            Assert.Equal(HttpStatusCode.Forbidden, fhirException.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAUserWithHardDeletePermissions_WhenHardDeletingAResource_TheServerShouldReturnSuccess()
        {
            TestFhirClient tempClient = _client.CreateClientForUser(TestUsers.ReadWriteUser, TestApplications.NativeClient);
            Observation createdResource = await tempClient.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>());

            tempClient = _client.CreateClientForUser(TestUsers.AdminUser, TestApplications.NativeClient);

            // Hard-delete the resource.
            await tempClient.HardDeleteAsync(createdResource);

            tempClient = _client.CreateClientForUser(TestUsers.ReadOnlyUser, TestApplications.NativeClient);

            // Getting the resource should result in NotFound.
            await ExecuteAndValidateNotFoundStatus(() => tempClient.ReadAsync<Observation>(ResourceType.Observation, createdResource.Id));

            async Task<FhirException> ExecuteAndValidateNotFoundStatus(Func<Task> action)
            {
                using FhirException exception = await Assert.ThrowsAsync<FhirException>(action);
                Assert.Equal(HttpStatusCode.NotFound, exception.StatusCode);
                return exception;
            }
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAUserWithUpdatePermissions_WhenUpdatingAResource_TheServerShouldReturnSuccess()
        {
            TestFhirClient tempClient = _client.CreateClientForUser(TestUsers.AdminUser, TestApplications.NativeClient);
            Observation createdResource = await tempClient.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>());

            tempClient = _client.CreateClientForUser(TestUsers.ReadWriteUser, TestApplications.NativeClient);
            using FhirResponse<Observation> updateResponse = await tempClient.UpdateAsync(createdResource);

            Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

            Observation updatedResource = updateResponse.Resource;

            Assert.NotNull(updatedResource);
            Assert.Equal(createdResource.Id, updatedResource.Id);
            Assert.NotEqual(createdResource.Meta.VersionId, updatedResource.Meta.VersionId);
            Assert.NotEqual(createdResource.Meta.LastUpdated, updatedResource.Meta.LastUpdated);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAClientWithNoAuthToken_WhenCreatingAResource_TheServerShouldReturnUnauthorized()
        {
            TestFhirClient tempClient = _client.CreateClientForClientApplication(TestApplications.InvalidClient);

            FhirException fhirException = await Assert.ThrowsAsync<FhirException>(async () => await tempClient.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>()));
            Assert.Equal(UnauthorizedMessage, fhirException.Message);
            Assert.Equal(HttpStatusCode.Unauthorized, fhirException.StatusCode);

            List<AuthenticationHeaderValue> wwwAuthenticationHeaderValues = fhirException.Headers.WwwAuthenticate.Where(h => h.Scheme == "Bearer").ToList();
            Assert.Single(wwwAuthenticationHeaderValues);

            Match matchResults = WwwAuthenticatePattern.Match(wwwAuthenticationHeaderValues.First().Parameter);

            Assert.Single(matchResults.Groups["authorization_uri"].Captures);
            var authorizationUri = matchResults.Groups["authorization_uri"].Captures[0].Value;
            Assert.Single(matchResults.Groups["realm"].Captures);
            var realm = matchResults.Groups["realm"].Captures[0].Value;
            Assert.Single(matchResults.Groups["resource_id"].Captures);
            var resourceId = matchResults.Groups["resource_id"].Captures[0].Value;

            Assert.Equal(AuthenticationSettings.Resource, realm);
            Assert.Equal(realm, resourceId);

            // We can only verify that this is a URI since a server with SmartOnFHIR enabled will not report the actual authorization server anywhere else.
            Assert.True(Uri.TryCreate(authorizationUri, UriKind.Absolute, out _));
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAClientWithInvalidAuthToken_WhenCreatingAResource_TheServerShouldReturnUnauthorized()
        {
            TestFhirClient tempClient = _client.CreateClientForClientApplication(TestApplications.InvalidClient).Clone();
            tempClient.SetBearerToken(InvalidToken);
            FhirException fhirException = await Assert.ThrowsAsync<FhirException>(async () => await tempClient.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>()));
            Assert.Equal(UnauthorizedMessage, fhirException.Message);
            Assert.Equal(HttpStatusCode.Unauthorized, fhirException.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAClientWithWrongAudience_WhenCreatingAResource_TheServerShouldReturnUnauthorized()
        {
            TestFhirClient tempClient = _client.CreateClientForClientApplication(TestApplications.WrongAudienceClient);
            FhirException fhirException = await Assert.ThrowsAsync<FhirException>(async () => await tempClient.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>()));
            Assert.Equal(UnauthorizedMessage, fhirException.Message);
            Assert.Equal(HttpStatusCode.Unauthorized, fhirException.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAUserWithReadPermissions_WhenGettingAResource_TheServerShouldReturnSuccess()
        {
            TestFhirClient tempClient = _client.CreateClientForClientApplication(TestApplications.GlobalAdminServicePrincipal);
            Observation createdResource = await tempClient.CreateAsync(Samples.GetDefaultObservation().ToPoco<Observation>());

            tempClient = _client.CreateClientForUser(TestUsers.ReadOnlyUser, TestApplications.NativeClient);
            using FhirResponse<Observation> readResponse = await tempClient.ReadAsync<Observation>(ResourceType.Observation, createdResource.Id);

            Observation readResource = readResponse.Resource;

            Assert.Equal(createdResource.Id, readResource.Id);
            Assert.Equal(createdResource.Meta.VersionId, readResource.Meta.VersionId);
            Assert.Equal(createdResource.Meta.LastUpdated, readResource.Meta.LastUpdated);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAUserWithNoExportPermissions_WhenExportResources_TheServerShouldReturnForbidden()
        {
            TestFhirClient tempClient = _client.CreateClientForUser(TestUsers.ReadOnlyUser, TestApplications.NativeClient);

            FhirException fhirException = await Assert.ThrowsAsync<FhirException>(async () => await tempClient.ExportAsync());
            Assert.Equal(ForbiddenMessage, fhirException.Message);
            Assert.Equal(HttpStatusCode.Forbidden, fhirException.StatusCode);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAUserWithExportPermissions_WhenExportResources_TheServerShouldReturnSuccess()
        {
            TestFhirClient tempClient = _client.CreateClientForUser(TestUsers.ExportUser, TestApplications.NativeClient);

            await tempClient.ExportAsync();
        }
    }
}
