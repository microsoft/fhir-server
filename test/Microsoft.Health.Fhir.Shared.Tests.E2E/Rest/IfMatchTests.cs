// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Extensions.Xunit;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Resources.Patch.FhirPathPatch.Helpers;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.Extensions;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    /// <summary>
    /// Verifies If-Match behavior for versioned-update resources.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class IfMatchTests : IClassFixture<HttpIntegrationTestFixture<StartupWithVersionedUpdateMedication>>
    {
        private const string MedicationResourceType = nameof(Medication);
        private const string FhirJsonMediaType = "application/fhir+json";
        private const string JsonPatchMediaType = "application/json-patch+json";

        private readonly HttpIntegrationTestFixture<StartupWithVersionedUpdateMedication> _fixture;
        private readonly TestFhirClient _client;

        public IfMatchTests(HttpIntegrationTestFixture<StartupWithVersionedUpdateMedication> fixture)
        {
            _fixture = fixture;
            _client = fixture.TestFhirClient;
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAVersionedMedication_WhenUpdatingWithIfMatch_ThenCurrentSucceedsAndStaleAndMissingAreRejected()
        {
            if (!_fixture.IsUsingInProcTestServer)
            {
                return;
            }

            Medication currentMedication = await CreateMedicationAsync();
            string currentText = CreateCodeText();
            SetCodeText(currentMedication, currentText);

            using (HttpResponseMessage response = await SendAsync(
                HttpMethod.Put,
                GetMedicationUri(currentMedication.Id),
                currentMedication,
                ifMatch: ToIfMatch(currentMedication)))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }

            Medication staleMedication = await CreateMedicationAsync();
            string staleIfMatch = ToIfMatch(staleMedication);
            string advancedText = CreateCodeText();
            Medication advancedMedication = await AdvanceMedicationAsync(staleMedication, advancedText);
            SetCodeText(advancedMedication, CreateCodeText());

            using (HttpResponseMessage response = await SendAsync(
                HttpMethod.Put,
                GetMedicationUri(advancedMedication.Id),
                advancedMedication,
                ifMatch: staleIfMatch))
            {
                Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
            }

            await AssertMedicationStateAsync(staleMedication.Id, advancedMedication.Meta.VersionId, advancedText);

            Medication missingMedication = await CreateMedicationAsync();
            string missingText = missingMedication.Code.Text;
            SetCodeText(missingMedication, CreateCodeText());

            using (HttpResponseMessage response = await SendAsync(
                HttpMethod.Put,
                GetMedicationUri(missingMedication.Id),
                missingMedication))
            {
                Assert.Equal(ExpectedMissingIfMatchStatus(), response.StatusCode);
            }

            await AssertMedicationStateAsync(missingMedication.Id, missingMedication.Meta.VersionId, missingText);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAVersionedMedication_WhenJsonPatchingWithIfMatch_ThenCurrentSucceedsAndStaleAndMissingAreRejected()
        {
            if (!_fixture.IsUsingInProcTestServer)
            {
                return;
            }

            Medication currentMedication = await CreateMedicationAsync();
            string currentText = CreateCodeText();

            using (HttpResponseMessage response = await SendAsync(
                HttpMethod.Patch,
                GetMedicationUri(currentMedication.Id),
                CreateJsonPatch(currentText),
                JsonPatchMediaType,
                ToIfMatch(currentMedication)))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }

            Medication staleMedication = await CreateMedicationAsync();
            string staleIfMatch = ToIfMatch(staleMedication);
            string advancedText = CreateCodeText();
            Medication advancedMedication = await AdvanceMedicationAsync(staleMedication, advancedText);

            using (HttpResponseMessage response = await SendAsync(
                HttpMethod.Patch,
                GetMedicationUri(advancedMedication.Id),
                CreateJsonPatch(CreateCodeText()),
                JsonPatchMediaType,
                staleIfMatch))
            {
                Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
            }

            await AssertMedicationStateAsync(staleMedication.Id, advancedMedication.Meta.VersionId, advancedText);

            Medication missingMedication = await CreateMedicationAsync();
            string missingText = missingMedication.Code.Text;

            using (HttpResponseMessage response = await SendAsync(
                HttpMethod.Patch,
                GetMedicationUri(missingMedication.Id),
                CreateJsonPatch(CreateCodeText()),
                JsonPatchMediaType))
            {
                Assert.Equal(ExpectedMissingIfMatchStatus(), response.StatusCode);
            }

            await AssertMedicationStateAsync(missingMedication.Id, missingMedication.Meta.VersionId, missingText);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAVersionedMedication_WhenFhirPatchingWithIfMatch_ThenCurrentSucceedsAndStaleAndMissingAreRejected()
        {
            if (!_fixture.IsUsingInProcTestServer)
            {
                return;
            }

            Medication currentMedication = await CreateMedicationAsync();
            string currentText = CreateCodeText();

            using (HttpResponseMessage response = await SendAsync(
                HttpMethod.Patch,
                GetMedicationUri(currentMedication.Id),
                CreateFhirPatch(currentText),
                ifMatch: ToIfMatch(currentMedication)))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }

            Medication staleMedication = await CreateMedicationAsync();
            string staleIfMatch = ToIfMatch(staleMedication);
            string advancedText = CreateCodeText();
            Medication advancedMedication = await AdvanceMedicationAsync(staleMedication, advancedText);

            using (HttpResponseMessage response = await SendAsync(
                HttpMethod.Patch,
                GetMedicationUri(advancedMedication.Id),
                CreateFhirPatch(CreateCodeText()),
                ifMatch: staleIfMatch))
            {
                Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
            }

            await AssertMedicationStateAsync(staleMedication.Id, advancedMedication.Meta.VersionId, advancedText);

            Medication missingMedication = await CreateMedicationAsync();
            string missingText = missingMedication.Code.Text;

            using (HttpResponseMessage response = await SendAsync(
                HttpMethod.Patch,
                GetMedicationUri(missingMedication.Id),
                CreateFhirPatch(CreateCodeText())))
            {
                Assert.Equal(ExpectedMissingIfMatchStatus(), response.StatusCode);
            }

            await AssertMedicationStateAsync(missingMedication.Id, missingMedication.Meta.VersionId, missingText);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAVersionedMedication_WhenUpdatingConditionallyWithIfMatch_ThenCurrentSucceedsAndStaleAndMissingAreRejected()
        {
            if (!_fixture.IsUsingInProcTestServer)
            {
                return;
            }

            Medication currentMedication = await CreateMedicationAsync();
            string currentText = CreateCodeText();
            SetCodeText(currentMedication, currentText);

            using (HttpResponseMessage response = await SendAsync(
                HttpMethod.Put,
                GetConditionalMedicationUri(currentMedication.Id),
                currentMedication,
                ifMatch: ToIfMatch(currentMedication)))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }

            Medication staleMedication = await CreateMedicationAsync();
            string staleIfMatch = ToIfMatch(staleMedication);
            string advancedText = CreateCodeText();
            Medication advancedMedication = await AdvanceMedicationAsync(staleMedication, advancedText);
            SetCodeText(advancedMedication, CreateCodeText());

            using (HttpResponseMessage response = await SendAsync(
                HttpMethod.Put,
                GetConditionalMedicationUri(advancedMedication.Id),
                advancedMedication,
                ifMatch: staleIfMatch))
            {
                Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
            }

            await AssertMedicationStateAsync(staleMedication.Id, advancedMedication.Meta.VersionId, advancedText);

            Medication missingMedication = await CreateMedicationAsync();
            string missingText = missingMedication.Code.Text;
            SetCodeText(missingMedication, CreateCodeText());

            using (HttpResponseMessage response = await SendAsync(
                HttpMethod.Put,
                GetConditionalMedicationUri(missingMedication.Id),
                missingMedication))
            {
                Assert.Equal(ExpectedMissingIfMatchStatus(), response.StatusCode);
            }

            await AssertMedicationStateAsync(missingMedication.Id, missingMedication.Meta.VersionId, missingText);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAVersionedMedication_WhenUpdatingConditionallyWithNoMatchAndNoIfMatch_ThenItIsCreated()
        {
            if (!_fixture.IsUsingInProcTestServer)
            {
                return;
            }

            Medication medication = CreateMedication();

            using (HttpResponseMessage response = await SendAsync(
                HttpMethod.Put,
                GetConditionalMedicationUri(medication.Id),
                medication))
            {
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            }

            Medication createdMedication = await ReadMedicationAsync(medication.Id);
            Assert.Equal(medication.Code.Text, createdMedication.Code.Text);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAVersionedMedication_WhenJsonPatchingConditionallyWithIfMatch_ThenCurrentSucceedsAndStaleAndMissingAreRejected()
        {
            if (!_fixture.IsUsingInProcTestServer)
            {
                return;
            }

            Medication currentMedication = await CreateMedicationAsync();
            string currentText = CreateCodeText();

            using (HttpResponseMessage response = await SendAsync(
                HttpMethod.Patch,
                GetConditionalMedicationUri(currentMedication.Id),
                CreateJsonPatch(currentText),
                JsonPatchMediaType,
                ToIfMatch(currentMedication)))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }

            Medication staleMedication = await CreateMedicationAsync();
            string staleIfMatch = ToIfMatch(staleMedication);
            string advancedText = CreateCodeText();
            Medication advancedMedication = await AdvanceMedicationAsync(staleMedication, advancedText);

            using (HttpResponseMessage response = await SendAsync(
                HttpMethod.Patch,
                GetConditionalMedicationUri(advancedMedication.Id),
                CreateJsonPatch(CreateCodeText()),
                JsonPatchMediaType,
                staleIfMatch))
            {
                Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
            }

            await AssertMedicationStateAsync(staleMedication.Id, advancedMedication.Meta.VersionId, advancedText);

            Medication missingMedication = await CreateMedicationAsync();
            string missingText = missingMedication.Code.Text;

            using (HttpResponseMessage response = await SendAsync(
                HttpMethod.Patch,
                GetConditionalMedicationUri(missingMedication.Id),
                CreateJsonPatch(CreateCodeText()),
                JsonPatchMediaType))
            {
                Assert.Equal(ExpectedMissingIfMatchStatus(), response.StatusCode);
            }

            await AssertMedicationStateAsync(missingMedication.Id, missingMedication.Meta.VersionId, missingText);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAVersionedMedication_WhenFhirPatchingConditionallyWithIfMatch_ThenCurrentSucceedsAndStaleAndMissingAreRejected()
        {
            if (!_fixture.IsUsingInProcTestServer)
            {
                return;
            }

            Medication currentMedication = await CreateMedicationAsync();
            string currentText = CreateCodeText();

            using (HttpResponseMessage response = await SendAsync(
                HttpMethod.Patch,
                GetConditionalMedicationUri(currentMedication.Id),
                CreateFhirPatch(currentText),
                ifMatch: ToIfMatch(currentMedication)))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }

            Medication staleMedication = await CreateMedicationAsync();
            string staleIfMatch = ToIfMatch(staleMedication);
            string advancedText = CreateCodeText();
            Medication advancedMedication = await AdvanceMedicationAsync(staleMedication, advancedText);

            using (HttpResponseMessage response = await SendAsync(
                HttpMethod.Patch,
                GetConditionalMedicationUri(advancedMedication.Id),
                CreateFhirPatch(CreateCodeText()),
                ifMatch: staleIfMatch))
            {
                Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
            }

            await AssertMedicationStateAsync(staleMedication.Id, advancedMedication.Meta.VersionId, advancedText);

            Medication missingMedication = await CreateMedicationAsync();
            string missingText = missingMedication.Code.Text;

            using (HttpResponseMessage response = await SendAsync(
                HttpMethod.Patch,
                GetConditionalMedicationUri(missingMedication.Id),
                CreateFhirPatch(CreateCodeText())))
            {
                Assert.Equal(ExpectedMissingIfMatchStatus(), response.StatusCode);
            }

            await AssertMedicationStateAsync(missingMedication.Id, missingMedication.Meta.VersionId, missingText);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAVersionedMedication_WhenSoftDeletingWithIfMatch_ThenCurrentSucceedsAndStaleAndMissingAreRejected()
        {
            if (!_fixture.IsUsingInProcTestServer)
            {
                return;
            }

            Medication currentMedication = await CreateMedicationAsync();

            using (HttpResponseMessage response = await SendAsync(
                HttpMethod.Delete,
                GetMedicationUri(currentMedication.Id),
                ifMatch: ToIfMatch(currentMedication)))
            {
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            }

            await AssertMedicationReadFailsAsync(currentMedication.Id, HttpStatusCode.Gone);

            Medication staleMedication = await CreateMedicationAsync();
            string staleIfMatch = ToIfMatch(staleMedication);
            string advancedText = CreateCodeText();
            Medication advancedMedication = await AdvanceMedicationAsync(staleMedication, advancedText);

            using (HttpResponseMessage response = await SendAsync(
                HttpMethod.Delete,
                GetMedicationUri(advancedMedication.Id),
                ifMatch: staleIfMatch))
            {
                Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
            }

            await AssertMedicationStateAsync(staleMedication.Id, advancedMedication.Meta.VersionId, advancedText);

            Medication missingMedication = await CreateMedicationAsync();
            string missingText = missingMedication.Code.Text;

            using (HttpResponseMessage response = await SendAsync(
                HttpMethod.Delete,
                GetMedicationUri(missingMedication.Id)))
            {
                Assert.Equal(ExpectedMissingIfMatchStatus(), response.StatusCode);
            }

            await AssertMedicationStateAsync(missingMedication.Id, missingMedication.Meta.VersionId, missingText);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAVersionedMedication_WhenHardDeletingWithIfMatch_ThenCurrentSucceedsAndStaleAndMissingAreRejected()
        {
            if (!_fixture.IsUsingInProcTestServer)
            {
                return;
            }

            Medication currentMedication = await CreateMedicationAsync();

            using (HttpResponseMessage response = await SendAsync(
                HttpMethod.Delete,
                GetMedicationUri(currentMedication.Id, "?hardDelete=true"),
                ifMatch: ToIfMatch(currentMedication)))
            {
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            }

            await AssertMedicationReadFailsAsync(currentMedication.Id, HttpStatusCode.NotFound);

            Medication staleMedication = await CreateMedicationAsync();
            string staleIfMatch = ToIfMatch(staleMedication);
            string advancedText = CreateCodeText();
            Medication advancedMedication = await AdvanceMedicationAsync(staleMedication, advancedText);

            using (HttpResponseMessage response = await SendAsync(
                HttpMethod.Delete,
                GetMedicationUri(advancedMedication.Id, "?hardDelete=true"),
                ifMatch: staleIfMatch))
            {
                Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
            }

            await AssertMedicationStateAsync(staleMedication.Id, advancedMedication.Meta.VersionId, advancedText);

            Medication missingMedication = await CreateMedicationAsync();
            string missingText = missingMedication.Code.Text;

            using (HttpResponseMessage response = await SendAsync(
                HttpMethod.Delete,
                GetMedicationUri(missingMedication.Id, "?hardDelete=true")))
            {
                Assert.Equal(ExpectedMissingIfMatchStatus(), response.StatusCode);
            }

            await AssertMedicationStateAsync(missingMedication.Id, missingMedication.Meta.VersionId, missingText);
        }

        [SkippableFact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAVersionedMedication_WhenPurgingHistoryWithIfMatch_ThenCurrentSucceedsAndStaleAndMissingAreRejected()
        {
            if (!_fixture.IsUsingInProcTestServer)
            {
                return;
            }

            Skip.IfNot(_fixture.TestFhirServer.Metadata.SupportsOperation("purge-history"), "$purge-history not enabled on this server");

            Medication currentMedication = await CreateMedicationAsync();
            string currentText = CreateCodeText();
            Medication currentMedicationAfterUpdate = await AdvanceMedicationAsync(currentMedication, currentText);

            using (HttpResponseMessage response = await SendAsync(
                HttpMethod.Delete,
                GetMedicationUri(currentMedicationAfterUpdate.Id, "/$purge-history"),
                ifMatch: ToIfMatch(currentMedicationAfterUpdate)))
            {
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            }

            await AssertMedicationStateAsync(currentMedication.Id, currentMedicationAfterUpdate.Meta.VersionId, currentText);

            // The current version must still be accessible via VRead.
            await AssertMedicationVReadSucceedsAsync(currentMedication.Id, currentMedicationAfterUpdate.Meta.VersionId);

            // The old (pre-update) version must no longer be accessible after purge-history.
            await AssertMedicationVReadFailsAsync(currentMedication.Id, currentMedication.Meta.VersionId, HttpStatusCode.NotFound);

            Medication staleMedication = await CreateMedicationAsync();
            string staleIfMatch = ToIfMatch(staleMedication);
            string advancedText = CreateCodeText();
            Medication advancedMedication = await AdvanceMedicationAsync(staleMedication, advancedText);

            using (HttpResponseMessage response = await SendAsync(
                HttpMethod.Delete,
                GetMedicationUri(advancedMedication.Id, "/$purge-history"),
                ifMatch: staleIfMatch))
            {
                Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
            }

            await AssertMedicationStateAsync(staleMedication.Id, advancedMedication.Meta.VersionId, advancedText);

            // No purge occurred, so the old (pre-update) version must still be accessible via VRead.
            await AssertMedicationVReadSucceedsAsync(staleMedication.Id, staleMedication.Meta.VersionId);

            Medication missingMedication = await CreateMedicationAsync();
            string missingText = CreateCodeText();
            Medication missingMedicationAfterUpdate = await AdvanceMedicationAsync(missingMedication, missingText);

            using (HttpResponseMessage response = await SendAsync(
                HttpMethod.Delete,
                GetMedicationUri(missingMedicationAfterUpdate.Id, "/$purge-history")))
            {
                Assert.Equal(ExpectedMissingIfMatchStatus(), response.StatusCode);
            }

            await AssertMedicationStateAsync(missingMedication.Id, missingMedicationAfterUpdate.Meta.VersionId, missingText);

            // No purge occurred, so the old (pre-update) version must still be accessible via VRead.
            await AssertMedicationVReadSucceedsAsync(missingMedication.Id, missingMedication.Meta.VersionId);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAVersionedMedication_WhenSoftDeletingConditionallyWithIfMatch_ThenCurrentSucceedsAndStaleAndMissingAreRejected()
        {
            if (!_fixture.IsUsingInProcTestServer)
            {
                return;
            }

            Medication currentMedication = await CreateMedicationAsync();

            using (HttpResponseMessage response = await SendAsync(
                HttpMethod.Delete,
                GetConditionalMedicationUri(currentMedication.Id),
                ifMatch: ToIfMatch(currentMedication)))
            {
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            }

            await AssertMedicationReadFailsAsync(currentMedication.Id, HttpStatusCode.Gone);

            Medication staleMedication = await CreateMedicationAsync();
            string staleIfMatch = ToIfMatch(staleMedication);
            string advancedText = CreateCodeText();
            Medication advancedMedication = await AdvanceMedicationAsync(staleMedication, advancedText);

            using (HttpResponseMessage response = await SendAsync(
                HttpMethod.Delete,
                GetConditionalMedicationUri(advancedMedication.Id),
                ifMatch: staleIfMatch))
            {
                Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
            }

            await AssertMedicationStateAsync(staleMedication.Id, advancedMedication.Meta.VersionId, advancedText);

            Medication missingMedication = await CreateMedicationAsync();
            string missingText = missingMedication.Code.Text;

            using (HttpResponseMessage response = await SendAsync(
                HttpMethod.Delete,
                GetConditionalMedicationUri(missingMedication.Id)))
            {
                Assert.Equal(ExpectedMissingIfMatchStatus(), response.StatusCode);
            }

            await AssertMedicationStateAsync(missingMedication.Id, missingMedication.Meta.VersionId, missingText);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        public async Task GivenAVersionedMedication_WhenHardDeletingConditionallyWithIfMatch_ThenCurrentSucceedsAndStaleAndMissingAreRejected()
        {
            if (!_fixture.IsUsingInProcTestServer)
            {
                return;
            }

            Medication currentMedication = await CreateMedicationAsync();

            using (HttpResponseMessage response = await SendAsync(
                HttpMethod.Delete,
                GetConditionalMedicationUri(currentMedication.Id, "&hardDelete=true"),
                ifMatch: ToIfMatch(currentMedication)))
            {
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            }

            await AssertMedicationReadFailsAsync(currentMedication.Id, HttpStatusCode.NotFound);

            Medication staleMedication = await CreateMedicationAsync();
            string staleIfMatch = ToIfMatch(staleMedication);
            string advancedText = CreateCodeText();
            Medication advancedMedication = await AdvanceMedicationAsync(staleMedication, advancedText);

            using (HttpResponseMessage response = await SendAsync(
                HttpMethod.Delete,
                GetConditionalMedicationUri(advancedMedication.Id, "&hardDelete=true"),
                ifMatch: staleIfMatch))
            {
                Assert.Equal(HttpStatusCode.PreconditionFailed, response.StatusCode);
            }

            await AssertMedicationStateAsync(staleMedication.Id, advancedMedication.Meta.VersionId, advancedText);

            Medication missingMedication = await CreateMedicationAsync();
            string missingText = missingMedication.Code.Text;

            using (HttpResponseMessage response = await SendAsync(
                HttpMethod.Delete,
                GetConditionalMedicationUri(missingMedication.Id, "&hardDelete=true")))
            {
                Assert.Equal(ExpectedMissingIfMatchStatus(), response.StatusCode);
            }

            await AssertMedicationStateAsync(missingMedication.Id, missingMedication.Meta.VersionId, missingText);
        }

        [Fact]
        [Trait(Traits.Priority, Priority.One)]
        [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
        public async Task GivenAVersionedMedication_WhenUpdatingInATransactionWithIfMatch_ThenCurrentSucceedsAndStaleIsRejected()
        {
            if (!_fixture.IsUsingInProcTestServer)
            {
                return;
            }

            Medication medication = await CreateMedicationAsync();
            string staleIfMatch = ToIfMatch(medication);
            string currentText = CreateCodeText();

            using (FhirResponse<Bundle> response = await _client.PostBundleAsync(CreateTransactionBundle(medication, staleIfMatch, currentText)))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                Assert.Equal("200", response.Resource.Entry[0].Response.Status);
            }

            Medication currentMedication = await ReadMedicationAsync(medication.Id);
            string staleText = CreateCodeText();

            using FhirClientException exception = await Assert.ThrowsAsync<FhirClientException>(
                () => _client.PostBundleAsync(CreateTransactionBundle(currentMedication, staleIfMatch, staleText)));
            Assert.Equal(HttpStatusCode.PreconditionFailed, exception.StatusCode);

            await AssertMedicationStateAsync(medication.Id, currentMedication.Meta.VersionId, currentText);
        }

        private static HttpStatusCode ExpectedMissingIfMatchStatus()
        {
#if Stu3
            return HttpStatusCode.PreconditionFailed;
#else
            return HttpStatusCode.BadRequest;
#endif
        }

        private async Task<HttpResponseMessage> SendAsync(
            HttpMethod method,
            string uri,
            Resource resource = null,
            string mediaType = null,
            string ifMatch = null)
        {
            using var request = new HttpRequestMessage(method, uri);
            if (resource != null)
            {
                request.Content = new StringContent(
                    resource.ToJson(),
                    Encoding.UTF8,
                    new MediaTypeHeaderValue(mediaType ?? FhirJsonMediaType));
            }

            if (ifMatch != null)
            {
                request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
            }

            return await _client.HttpClient.SendAsync(request);
        }

        private async Task<HttpResponseMessage> SendAsync(
            HttpMethod method,
            string uri,
            string content,
            string mediaType,
            string ifMatch = null)
        {
            using var request = new HttpRequestMessage(method, uri)
            {
                Content = new StringContent(content, Encoding.UTF8, new MediaTypeHeaderValue(mediaType)),
            };

            if (ifMatch != null)
            {
                request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
            }

            return await _client.HttpClient.SendAsync(request);
        }

        private async Task<Medication> CreateMedicationAsync()
        {
            using FhirResponse<Medication> response = await _client.CreateAsync(CreateMedication());
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return response.Resource;
        }

        private static Medication CreateMedication()
        {
            return new Medication
            {
                Id = Guid.NewGuid().ToString(),
                Code = new CodeableConcept(
                    "https://example.org/medications",
                    Guid.NewGuid().ToString(),
                    CreateCodeText()),
            };
        }

        private async Task<Medication> AdvanceMedicationAsync(Medication medication, string codeText)
        {
            SetCodeText(medication, codeText);

            using (HttpResponseMessage response = await SendAsync(
                HttpMethod.Put,
                GetMedicationUri(medication.Id),
                medication,
                ifMatch: ToIfMatch(medication)))
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }

            return await ReadMedicationAsync(medication.Id);
        }

        private async Task<Medication> ReadMedicationAsync(string medicationId)
        {
            using FhirResponse<Medication> response = await _client.ReadAsync<Medication>(ResourceType.Medication, medicationId);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return response.Resource;
        }

        private async Task AssertMedicationStateAsync(string medicationId, string expectedVersion, string expectedCodeText)
        {
            Medication medication = await ReadMedicationAsync(medicationId);
            Assert.Equal(expectedVersion, medication.Meta.VersionId);
            Assert.Equal(expectedCodeText, medication.Code.Text);
        }

        private async Task AssertMedicationReadFailsAsync(string medicationId, HttpStatusCode expectedStatusCode)
        {
            using FhirClientException exception = await Assert.ThrowsAsync<FhirClientException>(
                () => _client.ReadAsync<Medication>(ResourceType.Medication, medicationId));
            Assert.Equal(expectedStatusCode, exception.StatusCode);
        }

        private async Task AssertMedicationVReadSucceedsAsync(string medicationId, string versionId)
        {
            using FhirResponse<Medication> response = await _client.VReadAsync<Medication>(ResourceType.Medication, medicationId, versionId);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(versionId, response.Resource.Meta.VersionId);
        }

        private async Task AssertMedicationVReadFailsAsync(string medicationId, string versionId, HttpStatusCode expectedStatusCode)
        {
            using FhirClientException exception = await Assert.ThrowsAsync<FhirClientException>(
                () => _client.VReadAsync<Medication>(ResourceType.Medication, medicationId, versionId));
            Assert.Equal(expectedStatusCode, exception.StatusCode);
        }

        private static Bundle CreateTransactionBundle(Medication medication, string ifMatch, string codeText)
        {
            SetCodeText(medication, codeText);

            return new Bundle
            {
                Type = Bundle.BundleType.Transaction,
                Entry = new List<Bundle.EntryComponent>
                {
                    new Bundle.EntryComponent
                    {
                        Resource = medication,
                        Request = new Bundle.RequestComponent
                        {
                            Method = Bundle.HTTPVerb.PUT,
                            Url = GetMedicationUri(medication.Id),
                            IfMatch = ifMatch,
                        },
                    },
                },
            };
        }

        private static Parameters CreateFhirPatch(string codeText)
        {
            return new Parameters().AddReplacePatchParameter("Medication.code.text", new FhirString(codeText));
        }

        private static string CreateJsonPatch(string codeText)
        {
            return $"[{{\"op\":\"replace\",\"path\":\"/code/text\",\"value\":\"{codeText}\"}}]";
        }

        private static string GetMedicationUri(string medicationId, string suffix = null)
        {
            return $"{MedicationResourceType}/{medicationId}{suffix}";
        }

        private static string GetConditionalMedicationUri(string medicationId, string suffix = null)
        {
            return $"{MedicationResourceType}?_id={medicationId}{suffix}";
        }

        private static string ToIfMatch(Medication medication)
        {
            return WeakETag.FromVersionId(medication.Meta.VersionId).ToString();
        }

        private static void SetCodeText(Medication medication, string codeText)
        {
            medication.Code.Text = codeText;
        }

        private static string CreateCodeText()
        {
            return $"if-match-{Guid.NewGuid()}";
        }
    }
}
