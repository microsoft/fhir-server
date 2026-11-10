// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Features.Operations.Import.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json.Linq;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Import
{
    /// <summary>
    /// Imported references with ids longer than the maximum resource id length are indexed truncated, and the import
    /// status reports them per resource type and search parameter.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.FhirImport)]
    [Trait(Traits.Category, Categories.Import)]
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
    public class ImportReferenceIdLengthTests : IClassFixture<ImportTestFixture<StartupForImportTestProvider>>
    {
        private readonly ImportTestFixture<StartupForImportTestProvider> _fixture;

        public ImportReferenceIdLengthTests(ImportTestFixture<StartupForImportTestProvider> fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task GivenImportedResourcesWithOverlongReferenceIds_WhenTheImportCompletes_ThenTheStatusHasAnIssueWithTheCount()
        {
            string tag = Guid.NewGuid().ToString();
            string overlongId = Guid.NewGuid().ToString("N") + new string('a', 33);

            JObject status = await ImportAsync(
                CreatePatient(tag, overlongId),
                CreatePatient(tag, overlongId),
                CreatePatient(tag, "valid-practitioner"));

            JToken issue = Assert.Single(status["issues"]);
            Assert.Equal("information", issue["severity"].Value<string>());
            Assert.Equal("Patient", issue["type"].Value<string>());
            Assert.Equal(2, issue["count"].Value<long>());
            Assert.Equal(
                "Reference parameter 'general-practitioner' ID exceeds 64 characters. Index truncation applied after 64 characters, which may yield over-inclusive results",
                issue["diagnostics"].Value<string>());
        }

        [Fact]
        public async Task GivenAnImportWithADuplicateResource_WhenTheImportCompletes_ThenOnlyTheImportedResourceIsCounted()
        {
            string tag = Guid.NewGuid().ToString();
            Patient patient = CreatePatient(tag, Guid.NewGuid().ToString("N") + new string('a', 33));

            JObject status = await ImportAsync(patient, patient);

            Assert.Equal(1, Assert.Single(status["error"])["count"].Value<long>());
            JToken issue = Assert.Single(status["issues"]);
            Assert.Equal("Patient", issue["type"].Value<string>());
            Assert.Equal(1, issue["count"].Value<long>());
        }

        [Fact]
        public async Task GivenImportedResourcesWithoutOverlongReferenceIds_WhenTheImportCompletes_ThenTheStatusHasNoIssues()
        {
            string tag = Guid.NewGuid().ToString();

            JObject status = await ImportAsync(CreatePatient(tag, "valid-practitioner"));

            Assert.Null(status["issues"]);
        }

        private static Patient CreatePatient(string tag, string practitionerId)
        {
            var patient = new Patient { Id = Guid.NewGuid().ToString() };
            patient.Meta = new Meta();
            patient.Meta.Tag.Add(new Coding(null, tag));
            patient.GeneralPractitioner.Add(new ResourceReference($"Practitioner/{practitionerId}"));
            return patient;
        }

        private async System.Threading.Tasks.Task<JObject> ImportAsync(params Patient[] patients)
        {
            var serializer = new FhirJsonSerializer();
            var content = new StringBuilder();
            foreach (Patient patient in patients)
            {
                content.AppendLine(serializer.SerializeToString(patient));
            }

            (Uri location, string etag) = await ImportTestHelper.UploadFileAsync(content.ToString(), _fixture.StorageAccount);
            var request = new ImportRequest()
            {
                InputFormat = "application/fhir+ndjson",
                InputSource = new Uri("https://other-server.example.org"),
                StorageDetail = new ImportRequestStorageDetail() { Type = "azure-blob" },
                Input = new List<InputResource> { new InputResource() { Etag = etag, Url = location, Type = "Patient" } },
            };

            Uri checkLocation = await ImportTestHelper.CreateImportTaskAsync(_fixture.TestFhirClient, request);

            // A stalled import worker keeps returning 202, so bound the wait instead of polling forever.
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            HttpResponseMessage response;
            try
            {
                while ((response = await _fixture.TestFhirClient.CheckImportAsync(checkLocation, cancellationToken: timeout.Token)).StatusCode == HttpStatusCode.Accepted)
                {
                    response.Dispose();
                    await Task.Delay(TimeSpan.FromSeconds(2), timeout.Token);
                }
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                throw new TimeoutException($"Import {checkLocation} did not complete within 5 minutes.");
            }

            using (response)
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                return JObject.Parse(await response.Content.ReadAsStringAsync());
            }
        }
    }
}
