// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Operations.Import.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Fhir.Tests.E2E.Rest.Metric;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Import
{
    [Trait(Traits.OwningTeam, OwningTeam.FhirImport)]
    [Trait(Traits.Category, Categories.Import)]
    [HttpIntegrationFixtureArgumentSets(DataStore.SqlServer, Format.Json)]
    public class ImportRebuildIndexesTests : IClassFixture<ImportRebuildIndexesTestFixture>
    {
        private readonly TestFhirClient _client;
        private readonly MetricHandler _metricHandler;
        private readonly ImportRebuildIndexesTestFixture _fixture;

        public ImportRebuildIndexesTests(ImportRebuildIndexesTestFixture fixture)
        {
            _client = fixture.TestFhirClient;
            _metricHandler = fixture.MetricHandler;
            _fixture = fixture;
        }

        [Fact(Skip = "Disabling/rebuilding of indexes is disabled because every E2E $import test is affected. There are special tests still.")]
        public async Task GivenImportOperationEnabled_WhenRebuildIndexesEnabled_ThenAllIndexesShouldBeRebuild()
        {
            if (!_fixture.IsUsingInProcTestServer)
            {
                return;
            }

            _metricHandler?.ResetCount();
            TestFhirClient tempClient = _client.CreateClientForUser(TestUsers.BulkImportUser, TestApplications.NativeClient);
            string patientNdJsonResource = Samples.GetNdJson("Import-Patient");
            patientNdJsonResource = Regex.Replace(patientNdJsonResource, "##PatientID##", m => Guid.NewGuid().ToString("N"));
            (Uri location, string etag) = await ImportTestHelper.UploadFileAsync(patientNdJsonResource, _fixture.CloudStorageAccount);

            var request = new ImportRequest()
            {
                InputFormat = "application/fhir+ndjson",
                InputSource = new Uri("https://other-server.example.org"),
                StorageDetail = new ImportRequestStorageDetail() { Type = "azure-blob" },
                Input = new List<InputResource>()
                {
                    new InputResource()
                    {
                        Url = location,
                        Type = "Patient",
                        Etag = etag,
                    },
                },
            };

            await ImportCheckAsync(request, tempClient);

            var resourceCount = Regex.Matches(patientNdJsonResource, "{\"resourceType\":").Count;
            var notificationList = _metricHandler.NotificationMapping[typeof(ImportJobMetricsNotification)];
            Assert.Single(notificationList);
            var notification = notificationList.First() as ImportJobMetricsNotification;
            Assert.Equal(JobStatus.Completed.ToString(), notification.Status);
            Assert.NotNull(notification.DataSize);
            Assert.Equal(resourceCount, notification.SucceedCount);
            Assert.Equal(0, notification.FailedCount);
        }

        private async Task<Uri> ImportCheckAsync(ImportRequest request, TestFhirClient client = null, int? errorCount = null)
        {
            client = client ?? _client;
            Uri checkLocation = await ImportTestHelper.CreateImportTaskAsync(client, request);

            HttpResponseMessage response;
            while ((response = await client.CheckImportAsync(checkLocation, CancellationToken.None)).StatusCode == System.Net.HttpStatusCode.Accepted)
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
            }

            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            ImportJobResult result = JsonConvert.DeserializeObject<ImportJobResult>(await response.Content.ReadAsStringAsync());
            Assert.NotEmpty(result.Output);
            if (errorCount != null)
            {
                Assert.Equal(errorCount.Value, result.Error.First().Count);
            }
            else
            {
                Assert.Empty(result.Error);
            }

            Assert.NotEmpty(result.Request);

            return checkLocation;
        }
    }
}
