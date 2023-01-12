﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Api.Features.Operations.Import;
using Microsoft.Health.Fhir.Client;
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
    public class ImportTests : IClassFixture<ImportTestFixture<StartupForImportTestProvider>>
    {
        private const string ForbiddenMessage = "Forbidden: Authorization failed.";

        private readonly TestFhirClient _client;
        private readonly MetricHandler _metricHandler;
        private readonly ImportTestFixture<StartupForImportTestProvider> _fixture;

        public ImportTests(ImportTestFixture<StartupForImportTestProvider> fixture)
        {
            _client = fixture.TestFhirClient;
            _metricHandler = fixture.MetricHandler;
            _fixture = fixture;
        }

        [Fact]
        [Trait(Traits.Category, Categories.Authorization)]
        public async Task GivenAUserWithImportPermissions_WhenImportData_TheServerShouldReturnSuccess()
        {
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
                    },
                },
            };

            await ImportCheckAsync(request, tempClient);

            // Only check metric for local tests
            if (_fixture.IsUsingInProcTestServer)
            {
                var resourceCount = Regex.Matches(patientNdJsonResource, "{\"resourceType\":").Count;
                var notificationList = _metricHandler.NotificationMapping[typeof(ImportJobMetricsNotification)];
                Assert.Single(notificationList);
                var notification = notificationList.First() as ImportJobMetricsNotification;
                Assert.Equal(JobStatus.Completed.ToString(), notification.Status);
                Assert.NotNull(notification.DataSize);
                Assert.Equal(resourceCount, notification.SucceedCount);
                Assert.Equal(0, notification.FailedCount);
            }
        }

        [Fact]
        [Trait(Traits.Category, Categories.Authorization)]
        public async Task GivenAUserWithoutImportPermissions_WhenImportData_ThenServerShouldReturnForbidden()
        {
            TestFhirClient tempClient = _client.CreateClientForUser(TestUsers.ReadOnlyUser, TestApplications.NativeClient);
            string patientNdJsonResource = Samples.GetNdJson("Import-Patient");
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
                    },
                },
            };

            request.Mode = ImportConstants.InitialLoadMode;
            request.Force = true;
            FhirClientException fhirException = await Assert.ThrowsAsync<FhirClientException>(async () => await tempClient.ImportAsync(request.ToParameters(), CancellationToken.None));
            Assert.StartsWith(ForbiddenMessage, fhirException.Message);
            Assert.Equal(HttpStatusCode.Forbidden, fhirException.StatusCode);
        }

        [Fact]
        public async Task GivenImportOperationEnabled_WhenImportOperationTriggered_ThenDataShouldBeImported()
        {
            _metricHandler?.ResetCount();
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
                        Etag = etag,
                        Type = "Patient",
                    },
                },
            };

            await ImportCheckAsync(request);

            // Only check metric for local tests
            if (_fixture.IsUsingInProcTestServer)
            {
                var resourceCount = Regex.Matches(patientNdJsonResource, "{\"resourceType\":").Count;
                var notificationList = _metricHandler.NotificationMapping[typeof(ImportJobMetricsNotification)];
                Assert.Single(notificationList);
                var notification = notificationList.First() as ImportJobMetricsNotification;
                Assert.Equal(JobStatus.Completed.ToString(), notification.Status);
                Assert.NotNull(notification.DataSize);
                Assert.Equal(resourceCount, notification.SucceedCount);
                Assert.Equal(0, notification.FailedCount);
            }
        }

        [Fact]
        public async Task GivenImportOperationEnabled_WhenImportOperationTriggeredBeforePreviousTaskCompleted_ThenConflictShouldBeReturned()
        {
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
                        Etag = etag,
                        Type = "Patient",
                    },
                },
            };

            request.Mode = ImportConstants.InitialLoadMode;
            request.Force = true;
            Uri checkLocation = await ImportTestHelper.CreateImportTaskAsync(_client, request);
            FhirClientException fhirException = await Assert.ThrowsAsync<FhirClientException>(async () => await _client.ImportAsync(request.ToParameters(), CancellationToken.None));
            Assert.Equal(HttpStatusCode.Conflict, fhirException.StatusCode);

            HttpResponseMessage response;
            while ((response = await _client.CheckImportAsync(checkLocation, CancellationToken.None)).StatusCode == System.Net.HttpStatusCode.Accepted)
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
        }

        [Fact]
        public async Task GivenImportOperationEnabled_WhenImportOperationTriggeredWithoutEtag_ThenDataShouldBeImported()
        {
            _metricHandler?.ResetCount();
            string patientNdJsonResource = Samples.GetNdJson("Import-Patient");
            patientNdJsonResource = Regex.Replace(patientNdJsonResource, "##PatientID##", m => Guid.NewGuid().ToString("N"));
            (Uri location, string _) = await ImportTestHelper.UploadFileAsync(patientNdJsonResource, _fixture.CloudStorageAccount);

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
                    },
                },
            };

            await ImportCheckAsync(request);

            // Only check metric for local tests
            if (_fixture.IsUsingInProcTestServer)
            {
                var resourceCount = Regex.Matches(patientNdJsonResource, "{\"resourceType\":").Count;
                var notificationList = _metricHandler.NotificationMapping[typeof(ImportJobMetricsNotification)];
                Assert.Single(notificationList);
                var notification = notificationList.First() as ImportJobMetricsNotification;
                Assert.Equal(JobStatus.Completed.ToString(), notification.Status);
                Assert.NotNull(notification.DataSize);
                Assert.Equal(resourceCount, notification.SucceedCount);
                Assert.Equal(0, notification.FailedCount);
            }
        }

        [Fact]
        public async Task GivenImportOperationEnabled_WhenImportResourceWithWrongType_ThenErrorLogShouldBeUploaded()
        {
            _metricHandler?.ResetCount();
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
                        Etag = etag,
                        Type = "Observation", // not match the resource
                    },
                },
            };

            Uri checkLocation = await ImportTestHelper.CreateImportTaskAsync(_client, request);

            HttpResponseMessage response;
            while ((response = await _client.CheckImportAsync(checkLocation, CancellationToken.None)).StatusCode == System.Net.HttpStatusCode.Accepted)
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
            }

            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            ImportJobResult result = JsonConvert.DeserializeObject<ImportJobResult>(await response.Content.ReadAsStringAsync());
            Assert.Single(result.Error);
            Assert.NotEmpty(result.Error.First().Url);

            // Only check metric for local tests
            if (_fixture.IsUsingInProcTestServer)
            {
                var resourceCount = Regex.Matches(patientNdJsonResource, "{\"resourceType\":").Count;
                var notificationList = _metricHandler.NotificationMapping[typeof(ImportJobMetricsNotification)];
                Assert.Single(notificationList);
                var notification = notificationList.First() as ImportJobMetricsNotification;
                Assert.Equal(JobStatus.Completed.ToString(), notification.Status);
                Assert.NotNull(notification.DataSize);
                Assert.Equal(0, notification.SucceedCount);
                Assert.Equal(resourceCount, notification.FailedCount);
            }
        }

        [Fact]
        public async Task GivenImportOperationEnabled_WhenImportOperationTriggeredWithMultipleFiles_ThenDataShouldBeImported()
        {
            _metricHandler?.ResetCount();
            string patientNdJsonResource = Samples.GetNdJson("Import-SinglePatientTemplate");
            string resourceId1 = Guid.NewGuid().ToString("N");
            string patientNdJsonResource1 = patientNdJsonResource.Replace("##PatientID##", resourceId1);
            string resourceId2 = Guid.NewGuid().ToString("N");
            string patientNdJsonResource2 = patientNdJsonResource.Replace("##PatientID##", resourceId2);

            (Uri location1, string _) = await ImportTestHelper.UploadFileAsync(patientNdJsonResource1, _fixture.CloudStorageAccount);
            (Uri location2, string _) = await ImportTestHelper.UploadFileAsync(patientNdJsonResource2, _fixture.CloudStorageAccount);

            var request = new ImportRequest()
            {
                InputFormat = "application/fhir+ndjson",
                InputSource = new Uri("https://other-server.example.org"),
                StorageDetail = new ImportRequestStorageDetail() { Type = "azure-blob" },
                Input = new List<InputResource>()
                {
                    new InputResource()
                    {
                        Url = location1,
                        Type = "Patient",
                    },
                    new InputResource()
                    {
                        Url = location2,
                        Type = "Patient",
                    },
                },
            };

            await ImportCheckAsync(request);

            // Only check metric for local tests
            if (_fixture.IsUsingInProcTestServer)
            {
                var resourceCount = Regex.Matches(patientNdJsonResource, "{\"resourceType\":").Count * 2;
                var notificationList = _metricHandler.NotificationMapping[typeof(ImportJobMetricsNotification)];
                Assert.Single(notificationList);
                var notification = notificationList.First() as ImportJobMetricsNotification;
                Assert.Equal(JobStatus.Completed.ToString(), notification.Status);
                Assert.NotNull(notification.DataSize);
                Assert.Equal(resourceCount, notification.SucceedCount);
                Assert.Equal(0, notification.FailedCount);
            }
        }

        [Fact]
        public async Task GivenImportOperationEnabled_WhenImportInvalidResource_ThenErrorLogsShouldBeOutput()
        {
            _metricHandler?.ResetCount();
            string patientNdJsonResource = Samples.GetNdJson("Import-InvalidPatient");
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
                        Etag = etag,
                        Type = "Patient",
                    },
                },
            };

            Uri checkLocation = await ImportTestHelper.CreateImportTaskAsync(_client, request);

            HttpResponseMessage response;
            while ((response = await _client.CheckImportAsync(checkLocation, CancellationToken.None)).StatusCode == System.Net.HttpStatusCode.Accepted)
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
            }

            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            ImportJobResult result = JsonConvert.DeserializeObject<ImportJobResult>(await response.Content.ReadAsStringAsync());
            Assert.NotEmpty(result.Output);
            Assert.Equal(1, result.Error.Count);
            Assert.NotEmpty(result.Request);

            string errorLoation = result.Error.ToArray()[0].Url;
            string[] errorContents = (await ImportTestHelper.DownloadFileAsync(errorLoation, _fixture.CloudStorageAccount)).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            Assert.Single(errorContents);

            // Only check metric for local tests
            if (_fixture.IsUsingInProcTestServer)
            {
                var resourceCount = Regex.Matches(patientNdJsonResource, "{\"resourceType\":").Count;
                var notificationList = _metricHandler.NotificationMapping[typeof(ImportJobMetricsNotification)];
                Assert.Single(notificationList);
                var notification = notificationList.First() as ImportJobMetricsNotification;
                Assert.Equal(JobStatus.Completed.ToString(), notification.Status);
                Assert.NotNull(notification.DataSize);
                Assert.Equal(resourceCount, notification.SucceedCount);
                Assert.Equal(1, notification.FailedCount);
            }
        }

        [Fact]
        public async Task GivenImportOperationEnabled_WhenImportDuplicatedResource_ThenDupResourceShouldBeCleaned()
        {
            _metricHandler?.ResetCount();
            string patientNdJsonResource = Samples.GetNdJson("Import-DupPatientTemplate");
            string resourceId = Guid.NewGuid().ToString("N");
            patientNdJsonResource = patientNdJsonResource.Replace("##PatientID##", resourceId);
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
                        Etag = etag,
                        Type = "Patient",
                    },
                },
            };

            await ImportCheckAsync(request, errorCount: 1);
            await ImportCheckAsync(request, errorCount: 2);

            Patient patient = await _client.ReadAsync<Patient>(ResourceType.Patient, resourceId);
            Assert.Equal(resourceId, patient.Id);

            // Only check metric for local tests
            if (_fixture.IsUsingInProcTestServer)
            {
                var notificationList = _metricHandler.NotificationMapping[typeof(ImportJobMetricsNotification)];
                Assert.Equal(2, notificationList.Count);

                var notification1 = notificationList.First() as ImportJobMetricsNotification;
                Assert.Equal(JobStatus.Completed.ToString(), notification1.Status);
                Assert.Equal(1, notification1.SucceedCount);
                Assert.Equal(1, notification1.FailedCount);

                var notification2 = notificationList[1] as ImportJobMetricsNotification;
                Assert.Equal(JobStatus.Completed.ToString(), notification1.Status);
                Assert.Equal(0, notification2.SucceedCount);
                Assert.Equal(2, notification2.FailedCount);
            }
        }

        [Fact]
        public async Task GivenImportOperationEnabled_WhenCancelImportTask_ThenTaskShouldBeCanceled()
        {
            _metricHandler?.ResetCount();
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
                        Etag = etag,
                        Type = "Patient",
                    },
                },
            };

            Uri checkLocation = await ImportTestHelper.CreateImportTaskAsync(_client, request);
            var respone = await _client.CancelImport(checkLocation);

            // wait task completed
            while (respone.StatusCode != HttpStatusCode.Conflict)
            {
                respone = await _client.CancelImport(checkLocation);
                await Task.Delay(TimeSpan.FromSeconds(3));
            }

            FhirClientException fhirException = await Assert.ThrowsAsync<FhirClientException>(async () => await _client.CheckImportAsync(checkLocation));
            Assert.Equal(HttpStatusCode.BadRequest, fhirException.StatusCode);
        }

        [Fact(Skip = "long running tests for invalid url")]
        public async Task GivenImportOperationEnabled_WhenImportInvalidResourceUrl_ThenBadRequestShouldBeReturned()
        {
            _metricHandler?.ResetCount();
            var request = new ImportRequest()
            {
                InputFormat = "application/fhir+ndjson",
                InputSource = new Uri("https://other-server.example.org"),
                StorageDetail = new ImportRequestStorageDetail() { Type = "azure-blob" },
                Input = new List<InputResource>()
                {
                    new InputResource()
                    {
                        Url = new Uri("https://fhirtest-invalid.com"),
                        Type = "Patient",
                    },
                },
            };

            Uri checkLocation = await ImportTestHelper.CreateImportTaskAsync(_client, request);

            FhirClientException fhirException = await Assert.ThrowsAsync<FhirClientException>(
                async () =>
                {
                    HttpResponseMessage response;
                    while ((response = await _client.CheckImportAsync(checkLocation, CancellationToken.None)).StatusCode == System.Net.HttpStatusCode.Accepted)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5));
                    }
                });
            Assert.Equal(HttpStatusCode.BadRequest, fhirException.StatusCode);

            // Only check metric for local tests
            if (_fixture.IsUsingInProcTestServer)
            {
                var notificationList = _metricHandler.NotificationMapping[typeof(ImportJobMetricsNotification)];
                Assert.Single(notificationList);
                var notification = notificationList.First() as ImportJobMetricsNotification;
                Assert.Equal(JobStatus.Failed.ToString(), notification.Status);
                Assert.Null(notification.DataSize);
                Assert.Null(notification.SucceedCount);
                Assert.Null(notification.FailedCount);
            }
        }

        [Fact]
        public async Task GivenImportOperationEnabled_WhenImportInvalidETag_ThenBadRequestShouldBeReturned()
        {
            _metricHandler?.ResetCount();
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
                        Etag = "invalid",
                        Type = "Patient",
                    },
                },
            };

            Uri checkLocation = await ImportTestHelper.CreateImportTaskAsync(_client, request);

            FhirClientException fhirException = await Assert.ThrowsAsync<FhirClientException>(
                async () =>
                {
                    HttpResponseMessage response;
                    while ((response = await _client.CheckImportAsync(checkLocation, CancellationToken.None)).StatusCode == System.Net.HttpStatusCode.Accepted)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5));
                    }
                });
            Assert.Equal(HttpStatusCode.BadRequest, fhirException.StatusCode);

            // Only check metric for local tests
            if (_fixture.IsUsingInProcTestServer)
            {
                var notificationList = _metricHandler.NotificationMapping[typeof(ImportJobMetricsNotification)];
                Assert.Single(notificationList);
                var notification = notificationList.First() as ImportJobMetricsNotification;
                Assert.Equal(JobStatus.Failed.ToString(), notification.Status);
                Assert.Null(notification.DataSize);
                Assert.Equal(0, notification.SucceedCount);
                Assert.Equal(0, notification.FailedCount);
            }
        }

        [Fact]
        public async Task GivenImportOperationEnabled_WhenImportInvalidResourceType_ThenBadRequestShouldBeReturned()
        {
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
                        Type = "Invalid",
                    },
                },
            };

            FhirClientException fhirException = await Assert.ThrowsAsync<FhirClientException>(
                async () => await ImportTestHelper.CreateImportTaskAsync(_client, request));

            Assert.Equal(HttpStatusCode.BadRequest, fhirException.StatusCode);
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
