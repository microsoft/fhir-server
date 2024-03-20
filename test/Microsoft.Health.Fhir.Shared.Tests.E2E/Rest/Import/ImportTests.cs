// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using IdentityServer4.Models;
using MediatR;
using Microsoft.Health.Fhir.Api.Features.Operations.Import;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Operations.Import.Models;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Microsoft.Health.Fhir.Tests.E2E.Rest.Metric;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using Xunit;
using static Hl7.Fhir.Model.Bundle;
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
        private readonly MetricHandler _bundleMetricHandler;
        private readonly ImportTestFixture<StartupForImportTestProvider> _fixture;

        public ImportTests(ImportTestFixture<StartupForImportTestProvider> fixture)
        {
            _client = fixture.TestFhirClient;
            _metricHandler = fixture.MetricHandler;
            _bundleMetricHandler = fixture.BundleMetricHandler;
            _fixture = fixture;
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenIncrementalLoad_MultipleInputVersionsOutOfOrderSomeNotExplicit_ResourceNotExisting_NoGap(bool useBundleEndpoint)
        {
            var id = Guid.NewGuid().ToString("N");
            var ndJson = PrepareResource(id, "1", "2001");
            var ndJson2 = PrepareResource(id, null, "2002");
            var ndJson3 = PrepareResource(id, "2", "2003");

            await Import(ndJson + ndJson2 + ndJson3, 2, 1, useBundleEndpoint);

            // check current
            var result = await _client.ReadAsync<Patient>(ResourceType.Patient, id);
            Assert.Equal("2", result.Resource.Meta.VersionId);
            Assert.Equal(GetLastUpdated("2003"), result.Resource.Meta.LastUpdated);

            // check history
            result = await _client.VReadAsync<Patient>(ResourceType.Patient, id, "1");
            Assert.Equal(GetLastUpdated("2001"), result.Resource.Meta.LastUpdated);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenIncrementalLoad_MultipleInputVersionsOutOfOrderSomeNotExplicit_ResourceNotExisting(bool useBundleEndpoint)
        {
            var id = Guid.NewGuid().ToString("N");
            var ndJson = PrepareResource(id, "1", "2001");
            var ndJson2 = PrepareResource(id, null, "2002");
            var ndJson3 = PrepareResource(id, "3", "2003");

            await Import(ndJson + ndJson2 + ndJson3, 3, 0, useBundleEndpoint);

            // check current
            var result = await _client.ReadAsync<Patient>(ResourceType.Patient, id);
            Assert.Equal("3", result.Resource.Meta.VersionId);
            Assert.Equal(GetLastUpdated("2003"), result.Resource.Meta.LastUpdated);

            // check history
            result = await _client.VReadAsync<Patient>(ResourceType.Patient, id, "1");
            Assert.Equal(GetLastUpdated("2001"), result.Resource.Meta.LastUpdated);
            result = await _client.VReadAsync<Patient>(ResourceType.Patient, id, "2");
            Assert.Equal(GetLastUpdated("2002"), result.Resource.Meta.LastUpdated);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenImportBundle_InvalidResourceType_FailureReturned(bool isNdJson)
        {
            var ndJson = Samples.GetNdJson("Import-SinglePatientTemplate");
            ndJson = ndJson.Replace("Patient", "InvalidPatient"); // invalid resource type
            var response = await _client.ImportBundleAsync(isNdJson ? ndJson : DressAsImportBundle(new[] { ndJson }.Select(_ => DressAsBundleEntry(_))), isNdJson);
            Assert.Equal(isNdJson ? HttpStatusCode.OK : HttpStatusCode.BadRequest, response.StatusCode);
            var error = await response.Content.ReadAsStringAsync();
            if (isNdJson)
            {
                Assert.True(error.Contains("Failed to process resource at line: 0 with stream start offset: 0", StringComparison.OrdinalIgnoreCase));
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenImportBundle_InvalidResourceId_FailureReturned(bool isNdJson)
        {
            var ndJson = Samples.GetNdJson("Import-SinglePatientTemplate");
            ndJson = ndJson.Replace("##PatientID##", string.Empty); // invalid resource id
            var response = await _client.ImportBundleAsync(isNdJson ? ndJson : DressAsImportBundle(new[] { ndJson }.Select(_ => DressAsBundleEntry(_))), isNdJson);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var error = await response.Content.ReadAsStringAsync();
            Assert.True(error.Contains("Resource id is empty", StringComparison.OrdinalIgnoreCase));
////{
////  "resourceType": "OperationOutcome",
////  "issue": [
////    {
////      "severity": "error",
////      "details": { "text": "Resource id is empty" },
////      "diagnostics": "Failed to process resource at line: 0 with stream start offset: 0"
////    }
////  ]
////}
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenImportBundle_InvalidJson_FailureReturned(bool isNdJson)
        {
            var ndJson = Samples.GetNdJson("Import-SinglePatientTemplate");
            ndJson = ndJson.Substring(70); // invalid json
            var response = await _client.ImportBundleAsync(isNdJson ? ndJson : DressAsImportBundle(new[] { ndJson }.Select(_ => DressAsBundleEntry(_))), isNdJson);
            Assert.Equal(isNdJson ? HttpStatusCode.OK : HttpStatusCode.BadRequest, response.StatusCode);
            var error = await response.Content.ReadAsStringAsync();
            if (isNdJson)
            {
                Assert.True(error.Contains("Failed to process resource at line: 0 with stream start offset: 0", StringComparison.OrdinalIgnoreCase));
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenImportBundle_MultipleResoureTypesInSingleCall_Success(bool isNdJson)
        {
            var ndJson1 = Samples.GetNdJson("Import-SinglePatientTemplate");
            var pid = Guid.NewGuid().ToString("N");
            ndJson1 = ndJson1.Replace("##PatientID##", pid);
            var ndJson2 = Samples.GetNdJson("Import-Observation");
            var oid = Guid.NewGuid().ToString("N");
            ndJson2 = ndJson2.Replace("##ObservationID##", oid).Replace("##PatientID##", pid);

            await Import(new[] { ndJson1, ndJson2 }, isNdJson);

            var patient = await _client.ReadAsync<Patient>(ResourceType.Patient, pid);
            Assert.Equal("1", patient.Resource.Meta.VersionId);
            var observation = await _client.ReadAsync<Observation>(ResourceType.Observation, oid);
            Assert.Equal("1", observation.Resource.Meta.VersionId);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenIncrementalLoad_MultipleResoureTypesInSingleFile_Success(bool useBundleEndpoint)
        {
            var ndJson1 = Samples.GetNdJson("Import-SinglePatientTemplate");
            var pid = Guid.NewGuid().ToString("N");
            ndJson1 = ndJson1.Replace("##PatientID##", pid);
            var ndJson2 = Samples.GetNdJson("Import-Observation");
            var oid = Guid.NewGuid().ToString("N");
            ndJson2 = ndJson2.Replace("##ObservationID##", oid).Replace("##PatientID##", pid);

            await Import(ndJson1 + ndJson2, 2, 0, useBundleEndpoint);

            var patient = await _client.ReadAsync<Patient>(ResourceType.Patient, pid);
            Assert.Equal("1", patient.Resource.Meta.VersionId);
            var observation = await _client.ReadAsync<Observation>(ResourceType.Observation, oid);
            Assert.Equal("1", observation.Resource.Meta.VersionId);
        }

        private async Task Import(IEnumerable<string> ndJsons, bool isNdJson)
        {
            var response = await _client.ImportBundleAsync(isNdJson ? string.Join(string.Empty, ndJsons) : DressAsImportBundle(ndJsons.Select(_ => DressAsBundleEntry(_))), isNdJson);
            Assert.True(response.IsSuccessStatusCode);
            CheckLoadedResources(response, ndJsons.Count(), 0);
        }

        private void CheckLoadedResources(HttpResponseMessage response, int loadedResources, int errors)
        {
            var result = JsonConvert.DeserializeObject<ImportBundleResult>(response.Content.ReadAsStringAsync().Result);
            Assert.Equal(loadedResources, result.LoadedResources);
            Assert.Equal(errors, result.Errors.Count);
        }

        private static (string ResourceType, string ResourceId) GetResourceKey(string jsonString, bool check)
        {
            var idStart = jsonString.IndexOf("\"id\":\"", StringComparison.OrdinalIgnoreCase) + 6;
            var idShort = jsonString.Substring(idStart, 50);
            var idEnd = idShort.IndexOf('"', StringComparison.OrdinalIgnoreCase);
            var resourceId = idShort.Substring(0, idEnd);
            if (check && string.IsNullOrEmpty(resourceId))
            {
                throw new ArgumentException("Cannot parse resource id with string parser");
            }

            var rtStart = jsonString.IndexOf("\"resourceType\":\"", StringComparison.OrdinalIgnoreCase) + 16;
            var rtShort = jsonString.Substring(rtStart, 50);
            var rtEnd = rtShort.IndexOf('"', StringComparison.OrdinalIgnoreCase);
            var resourceType = rtShort.Substring(0, rtEnd);
            if (check && string.IsNullOrEmpty(resourceType))
            {
                throw new ArgumentException("Cannot parse resource type with string parser");
            }

            return (resourceType, resourceId);
        }

        private static string DressAsBundleEntry(string jsonString, bool makeValidBundle = false)
        {
            var key = GetResourceKey(jsonString, makeValidBundle);
            var builder = new StringBuilder();
            builder.Append('{');
            if (makeValidBundle)
            {
                builder.Append(@"""fullUrl"":""").Append(key.ResourceType).Append('/').Append(key.ResourceId).Append('"').Append(',');
            }

            builder.Append(@"""resource"":").Append(jsonString);
            if (makeValidBundle)
            {
                builder.Append(',').Append(@"""request"":{""method"":""PUT"",""url"":""").Append(key.ResourceType).Append('/').Append(key.ResourceId).Append(@"""}");
            }

            builder.Append('}');
            return builder.ToString();
        }

        private static string DressAsImportBundle(IEnumerable<string> entries)
        {
            var builder = new StringBuilder();
            builder.Append(@"{""resourceType"":""Bundle"",""type"":""transaction"",""meta"":{""profile"":[""http://azurehealthcareapis.com/data-extensions/import-bundle""]},""entry"":[");
            var first = true;
            foreach (var entry in entries)
            {
                if (!first)
                {
                    builder.Append(',');
                }

                builder.Append(entry);
                first = false;
            }

            builder.Append(@"]}");
            return builder.ToString();
        }

        private async Task Import(string ndJson, int loaded, int errors, bool useBundleEndpoint)
        {
            if (useBundleEndpoint)
            {
                var response = await _client.ImportBundleAsync(ndJson, true);
                CheckLoadedResources(response, loaded, errors);
            }
            else
            {
                var location = (await ImportTestHelper.UploadFileAsync(ndJson, _fixture.StorageAccount)).location;
                var request = CreateImportRequest(location, ImportMode.IncrementalLoad, false);
                await ImportCheckAsync(request, null, errors);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenIncrementalLoad_MultipleNonSequentialInputVersions_ResourceExisting(bool setResourceType)
        {
            var id = Guid.NewGuid().ToString("N");

            // set existing
            var ndJson = PrepareResource(id, "10000", "2000");
            var location = (await ImportTestHelper.UploadFileAsync(ndJson, _fixture.StorageAccount)).location;
            var request = CreateImportRequest(location, ImportMode.IncrementalLoad, setResourceType);
            await ImportCheckAsync(request, null);

            // set input. something before and something after existing
            ndJson = PrepareResource(id, "9000", "1999");
            var ndJson2 = PrepareResource(id, "10100", "2001");
            var ndJson3 = PrepareResource(id, "10300", "2003");

            // note order of records
            location = (await ImportTestHelper.UploadFileAsync(ndJson2 + ndJson + ndJson3, _fixture.StorageAccount)).location;
            request = CreateImportRequest(location, ImportMode.IncrementalLoad, setResourceType);
            await ImportCheckAsync(request, null);

            // check current
            var result = await _client.ReadAsync<Patient>(ResourceType.Patient, id);
            Assert.Equal("10300", result.Resource.Meta.VersionId);
            Assert.Equal(GetLastUpdated("2003"), result.Resource.Meta.LastUpdated);

            // check history
            result = await _client.VReadAsync<Patient>(ResourceType.Patient, id, "9000");
            Assert.Equal(GetLastUpdated("1999"), result.Resource.Meta.LastUpdated);
            result = await _client.VReadAsync<Patient>(ResourceType.Patient, id, "10100");
            Assert.Equal(GetLastUpdated("2001"), result.Resource.Meta.LastUpdated);
            result = await _client.VReadAsync<Patient>(ResourceType.Patient, id, "10000");
            Assert.Equal(GetLastUpdated("2000"), result.Resource.Meta.LastUpdated);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenIncrementalLoad_MultipleInputVersions_ResourceExisting_VersionConflict(bool useBundleEndPoint)
        {
            var id = Guid.NewGuid().ToString("N");

            // set existing
            var ndJson2 = PrepareResource(id, "2", "2002");
            await Import(ndJson2, 1, 0, useBundleEndPoint);

            // set input
            var ndJson = PrepareResource(id, "1", "2001");
            //// keep ndJson2 as is
            var ndJson3 = PrepareResource(id, "3", "2003");
            await Import(ndJson + ndJson2 + ndJson3, 2, 1, useBundleEndPoint);

            // check current
            var result = await _client.ReadAsync<Patient>(ResourceType.Patient, id);
            Assert.Equal("3", result.Resource.Meta.VersionId);
            Assert.Equal(GetLastUpdated("2003"), result.Resource.Meta.LastUpdated);

            // check history
            result = await _client.VReadAsync<Patient>(ResourceType.Patient, id, "1");
            Assert.Equal(GetLastUpdated("2001"), result.Resource.Meta.LastUpdated);
            result = await _client.VReadAsync<Patient>(ResourceType.Patient, id, "2");
            Assert.Equal(GetLastUpdated("2002"), result.Resource.Meta.LastUpdated);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenIncrementalLoad_MultipleInputVersions_ResourceNotExisting(bool useBundleEndPoint)
        {
            var id = Guid.NewGuid().ToString("N");
            var ndJson = PrepareResource(id, "1", "2001");
            var ndJson2 = PrepareResource(id, "2", "2002");
            var ndJson3 = PrepareResource(id, "3", "2003");

            await Import(ndJson + ndJson2 + ndJson3, 3, 0, useBundleEndPoint);

            // check current
            var result = await _client.ReadAsync<Patient>(ResourceType.Patient, id);
            Assert.Equal("3", result.Resource.Meta.VersionId);
            Assert.Equal(GetLastUpdated("2003"), result.Resource.Meta.LastUpdated);

            // check history
            result = await _client.VReadAsync<Patient>(ResourceType.Patient, id, "1");
            Assert.Equal(GetLastUpdated("2001"), result.Resource.Meta.LastUpdated);
            result = await _client.VReadAsync<Patient>(ResourceType.Patient, id, "2");
            Assert.Equal(GetLastUpdated("2002"), result.Resource.Meta.LastUpdated);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenIncrementalImportInvalidResource_WhenImportData_ThenErrorLogsShouldBeOutputAndFailedCountShouldMatch(bool setResourceType)
        {
            _metricHandler?.ResetCount();
            string patientNdJsonResource = Samples.GetNdJson("Import-InvalidPatient");
            patientNdJsonResource = Regex.Replace(patientNdJsonResource, "##PatientID##", m => Guid.NewGuid().ToString("N"));
            (Uri location, string etag) = await ImportTestHelper.UploadFileAsync(patientNdJsonResource, _fixture.StorageAccount);

            var inputResource = new InputResource() { Url = location, Etag = etag };
            if (setResourceType)
            {
                inputResource.Type = "Patient";
            }

            var request = new ImportRequest()
            {
                InputFormat = "application/fhir+ndjson",
                InputSource = new Uri("https://other-server.example.org"),
                StorageDetail = new ImportRequestStorageDetail() { Type = "azure-blob" },
                Input = new List<InputResource>() { inputResource },
                Mode = ImportMode.IncrementalLoad.ToString(),
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
            Assert.Single(result.Error);
            Assert.NotEmpty(result.Request);

            string errorLocation = result.Error.ToArray()[0].Url;
            string[] errorContents = (await ImportTestHelper.DownloadFileAsync(errorLocation, _fixture.StorageAccount)).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            Assert.True(errorContents.Count() >= 1); // when run locally there might be duplicates. no idea why.

            // Only check metric for local tests
            if (_fixture.IsUsingInProcTestServer)
            {
                var resourceCount = Regex.Matches(patientNdJsonResource, "{\"resourceType\":").Count;
                var notificationList = _metricHandler.NotificationMapping[typeof(ImportJobMetricsNotification)];
                Assert.Single(notificationList);
                var notification = notificationList.First() as ImportJobMetricsNotification;
                Assert.Equal(JobStatus.Completed.ToString(), notification.Status);
                Assert.NotNull(notification.DataSize);
                Assert.Equal(resourceCount, notification.SucceededCount);
                Assert.Equal(1, notification.FailedCount);
                Assert.Equal(ImportMode.IncrementalLoad, notification.ImportMode);
            }
        }

        [Fact]
        [Trait(Traits.Category, Categories.Authorization)]
        public async Task GivenAUserWithoutImportPermissions_WhenImportData_ThenServerShouldReturnForbidden_WithNoImportNotification()
        {
            _metricHandler?.ResetCount();
            TestFhirClient tempClient = _client.CreateClientForClientApplication(TestApplications.ReadOnlyUser);
            string patientNdJsonResource = Samples.GetNdJson("Import-Patient");
            (Uri location, string etag) = await ImportTestHelper.UploadFileAsync(patientNdJsonResource, _fixture.StorageAccount);

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
                Mode = ImportMode.IncrementalLoad.ToString(),
            };

            request.Mode = ImportMode.IncrementalLoad.ToString();
            request.Force = true;
            FhirClientException fhirException = await Assert.ThrowsAsync<FhirClientException>(async () => await tempClient.ImportAsync(request.ToParameters(), CancellationToken.None));
            Assert.StartsWith(ForbiddenMessage, fhirException.Message);
            Assert.Equal(HttpStatusCode.Forbidden, fhirException.StatusCode);

            // Only check metric for local tests
            if (_fixture.IsUsingInProcTestServer)
            {
                List<INotification> notificationList;
                _metricHandler.NotificationMapping.TryGetValue(typeof(ImportJobMetricsNotification), out notificationList);
                Assert.Null(notificationList);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenIncrementalLoad_WhenSoftDeleted_ThenSoftDeletedShouldBePersisted(bool useBundleEndPoint)
        {
            var id = Guid.NewGuid().ToString("N");
            var ndJson = PrepareResource(id, "2", "2002", true);
            await Import(ndJson, 1, 0, useBundleEndPoint);

            var result = (await _client.SearchAsync($"Patient/{id}/_history")).Resource;
            Assert.Equal("2", result.Entry.First().Resource.VersionId);

            ndJson = PrepareResource(id, "1", "2001");
            await Import(ndJson, 1, 0, useBundleEndPoint);

            var patient = await _client.VReadAsync<Patient>(ResourceType.Patient, id, "1");
            Assert.NotNull(result);
            Assert.Equal(GetLastUpdated("2001"), patient.Resource.Meta.LastUpdated);

            ndJson = PrepareResource(id, "3", "2003");
            await Import(ndJson, 1, 0, useBundleEndPoint);

            patient = await _client.VReadAsync<Patient>(ResourceType.Patient, id, "3");
            Assert.NotNull(result);
            Assert.Equal(GetLastUpdated("2003"), patient.Resource.Meta.LastUpdated);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenIncrementalLoad_WhenOutOfOrder_ThenCurrentDatabaseVersionShouldRemain(bool useBundleEndPoint)
        {
            var id = Guid.NewGuid().ToString("N");
            var ndJson = PrepareResource(id, "2", "2002");
            await Import(ndJson, 1, 0, useBundleEndPoint);

            var result = await _client.ReadAsync<Patient>(ResourceType.Patient, id);
            Assert.NotNull(result);
            Assert.Equal(GetLastUpdated("2002"), result.Resource.Meta.LastUpdated);
            Assert.Equal("2", result.Resource.Meta.VersionId);

            ndJson = PrepareResource(id, "1", "2001");
            await Import(ndJson, 1, 0, useBundleEndPoint);

            result = await _client.ReadAsync<Patient>(ResourceType.Patient, id);
            Assert.NotNull(result);
            Assert.Equal(GetLastUpdated("2002"), result.Resource.Meta.LastUpdated); // nothing changes on 2nd import
            Assert.Equal("2", result.Resource.Meta.VersionId);

            result = await _client.VReadAsync<Patient>(ResourceType.Patient, id, "1");
            Assert.NotNull(result);
            Assert.Equal(GetLastUpdated("2001"), result.Resource.Meta.LastUpdated); // version 1 imported
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenIncrementalLoad_ThenInputLastUpdatedAndVersionShouldBeKept(bool useBundleEndPoint)
        {
            if (_fixture.IsUsingInProcTestServer)
            {
                _metricHandler.ResetCount();
                _bundleMetricHandler.ResetCount();
            }

            var id = Guid.NewGuid().ToString("N");
            var versionId = 2.ToString();
            var lastUpdatedYear = "2021";
            var lastUpdated = GetLastUpdated(lastUpdatedYear);
            var ndJson = PrepareResource(id, versionId, lastUpdatedYear);
            ndJson = ndJson + ndJson; // add one dup
            await Import(ndJson, 1, 1, useBundleEndPoint);

            var result = await _client.ReadAsync<Patient>(ResourceType.Patient, id);
            Assert.NotNull(result);
            Assert.Equal(lastUpdated, result.Resource.Meta.LastUpdated);
            Assert.Equal(versionId, result.Resource.Meta.VersionId);

            if (_fixture.IsUsingInProcTestServer)
            {
                if (useBundleEndPoint)
                {
                    var notificationList = _bundleMetricHandler.NotificationMapping[typeof(ImportBundleMetricsNotification)];
                    Assert.Single(notificationList);
                    var notification = (ImportBundleMetricsNotification)notificationList[0];
                    Assert.Equal(1, notification.SucceededCount);
                }
                else
                {
                    var notificationList = _metricHandler.NotificationMapping[typeof(ImportJobMetricsNotification)];
                    Assert.Single(notificationList);
                    var notification = (ImportJobMetricsNotification)notificationList[0];
                    Assert.Equal(1, notification.SucceededCount);
                }
            }
        }

        [Fact]
        public async Task GivenInitialLoad_ThenInputLastUpdatedAndVersionShouldNotBeKept()
        {
            var id = Guid.NewGuid().ToString("N");
            var versionId = 2.ToString();
            var lastUpdatedYear = "2021";
            var lastUpdated = GetLastUpdated(lastUpdatedYear);
            var ndJson = PrepareResource(id, versionId, lastUpdatedYear);
            ndJson = ndJson + ndJson; // add one dup
            (Uri location, string _) = await ImportTestHelper.UploadFileAsync(ndJson, _fixture.StorageAccount);

            var request = CreateImportRequest(location, ImportMode.InitialLoad);
            await ImportCheckAsync(request, null, 1);

            var result = await _client.ReadAsync<Patient>(ResourceType.Patient, id);
            Assert.NotNull(result);
            Assert.NotEqual(lastUpdated, result.Resource.Meta.LastUpdated);
            Assert.NotEqual(versionId, result.Resource.Meta.VersionId);
        }

        private static DateTimeOffset GetLastUpdated(string lastUpdatedYear)
        {
            return DateTimeOffset.Parse(lastUpdatedYear + "-01-01T00:00:00.000+00:00");
        }

        private static ImportRequest CreateImportRequest(Uri location, ImportMode importMode, bool setResourceType = true)
        {
            var inputResource = new InputResource() { Url = location };
            if (setResourceType)
            {
                inputResource.Type = "Patient";
            }

            return new ImportRequest()
            {
                InputFormat = "application/fhir+ndjson",
                InputSource = new Uri("https://other-server.example.org"),
                StorageDetail = new ImportRequestStorageDetail() { Type = "azure-blob" },
                Input = new List<InputResource>() { inputResource },
                Mode = importMode.ToString(),
            };
        }

        private static string PrepareResource(string id, string version, string lastUpdatedYear, bool isDeleted = false)
        {
            var ndJson = Samples.GetNdJson("Import-SinglePatientTemplate"); // "\"lastUpdated\":\"2020-01-01T00:00+00:00\"" "\"versionId\":\"1\"" "\"value\":\"654321\""
            ndJson = ndJson.Replace("##PatientID##", id);
            if (version != null)
            {
                ndJson = ndJson.Replace("\"versionId\":\"1\"", $"\"versionId\":\"{version}\"");
            }
            else
            {
                ndJson = ndJson.Replace("\"versionId\":\"1\",", string.Empty);
            }

            if (lastUpdatedYear != null)
            {
                ndJson = ndJson.Replace("\"lastUpdated\":\"2020-01-01T00:00:00.000+00:00\"", $"\"lastUpdated\":\"{lastUpdatedYear}-01-01T00:00:00.000+00:00\"");
            }
            else
            {
                ndJson = ndJson.Replace("\"lastUpdated\":\"2020-01-01T00:00:00.000+00:00\",", string.Empty);
            }

            if (isDeleted)
            {
                ndJson = ndJson.Replace(",\"meta\":{", ",\"meta\":{\"extension\":[{\"url\":\"http://azurehealthcareapis.com/data-extensions/deleted-state\",\"valueString\":\"soft-deleted\"}],");
            }

            return ndJson;
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        [Trait(Traits.Category, Categories.Authorization)]
        public async Task GivenAUserWithImportPermissions_WhenImportData_TheServerShouldReturnSuccess(bool setResourceType)
        {
            _metricHandler?.ResetCount();
            TestFhirClient tempClient = _client.CreateClientForClientApplication(TestApplications.BulkImportUser);
            string patientNdJsonResource = Samples.GetNdJson("Import-Patient");
            patientNdJsonResource = Regex.Replace(patientNdJsonResource, "##PatientID##", m => Guid.NewGuid().ToString("N"));
            (Uri location, string etag) = await ImportTestHelper.UploadFileAsync(patientNdJsonResource, _fixture.StorageAccount);

            var request = CreateImportRequest(location, ImportMode.InitialLoad, setResourceType);
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
                Assert.Equal(resourceCount, notification.SucceededCount);
                Assert.Equal(0, notification.FailedCount);
            }
        }

        [Fact]
        [Trait(Traits.Category, Categories.Authorization)]
        public async Task GivenAUserWithoutImportPermissions_WhenImportData_ThenServerShouldReturnForbidden()
        {
            TestFhirClient tempClient = _client.CreateClientForClientApplication(TestApplications.ReadOnlyUser);
            string patientNdJsonResource = Samples.GetNdJson("Import-Patient");
            (Uri location, string etag) = await ImportTestHelper.UploadFileAsync(patientNdJsonResource, _fixture.StorageAccount);

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
                Mode = ImportMode.InitialLoad.ToString(),
            };

            request.Mode = ImportMode.InitialLoad.ToString();
            request.Force = true;
            FhirClientException fhirException = await Assert.ThrowsAsync<FhirClientException>(async () => await tempClient.ImportAsync(request.ToParameters(), CancellationToken.None));
            Assert.StartsWith(ForbiddenMessage, fhirException.Message);
            Assert.Equal(HttpStatusCode.Forbidden, fhirException.StatusCode);
        }

        [Fact]
        public async Task GivenImportTriggered_ThenDataShouldBeImported()
        {
            _metricHandler?.ResetCount();
            string patientNdJsonResource = Samples.GetNdJson("Import-Patient");
            patientNdJsonResource = Regex.Replace(patientNdJsonResource, "##PatientID##", m => Guid.NewGuid().ToString("N"));
            (Uri location, string etag) = await ImportTestHelper.UploadFileAsync(patientNdJsonResource, _fixture.StorageAccount);

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
                Mode = ImportMode.InitialLoad.ToString(),
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
                Assert.Equal(resourceCount, notification.SucceededCount);
                Assert.Equal(0, notification.FailedCount);
            }
        }

        [Fact]
        public async Task GivenImportTriggered_WithAndWithoutETag_Then2ImportsShouldBeRegistered()
        {
            string patientNdJsonResource = Samples.GetNdJson("Import-Patient");
            patientNdJsonResource = Regex.Replace(patientNdJsonResource, "##PatientID##", m => Guid.NewGuid().ToString("N"));
            (Uri location, string etag) = await ImportTestHelper.UploadFileAsync(patientNdJsonResource, _fixture.StorageAccount);

            var request = new ImportRequest()
            {
                InputFormat = "application/fhir+ndjson",
                InputSource = new Uri("https://other-server.example.org"),
                StorageDetail = new ImportRequestStorageDetail() { Type = "azure-blob" },
                Input = new List<InputResource>() { new InputResource() { Url = location, Type = "Patient" } },
                Mode = ImportMode.IncrementalLoad.ToString(),
            };
            var checkLocation1 = await ImportTestHelper.CreateImportTaskAsync(_client, request);
            var checkLocation2 = await ImportTestHelper.CreateImportTaskAsync(_client, request);
            Assert.Equal(checkLocation1, checkLocation2); // idempotent registration
            await ImportWaitAsync(checkLocation1);

            request.Input = new List<InputResource>() { new InputResource() { Url = location, Type = "Patient", Etag = etag } };
            var checkLocation3 = await ImportTestHelper.CreateImportTaskAsync(_client, request);
            Assert.NotEqual(checkLocation1, checkLocation3);
            await ImportWaitAsync(checkLocation3);
        }

        [Fact]
        public async Task GivenImportOperationEnabled_WhenImportOperationTriggeredWithoutEtag_ThenDataShouldBeImported()
        {
            _metricHandler?.ResetCount();
            string patientNdJsonResource = Samples.GetNdJson("Import-Patient");
            patientNdJsonResource = Regex.Replace(patientNdJsonResource, "##PatientID##", m => Guid.NewGuid().ToString("N"));
            (Uri location, string _) = await ImportTestHelper.UploadFileAsync(patientNdJsonResource, _fixture.StorageAccount);

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
                Mode = ImportMode.InitialLoad.ToString(),
            };

            await ImportCheckAsync(request);

            // Only check metric for local tests
            if (_fixture.IsUsingInProcTestServer)
            {
                var resourceCount = Regex.Matches(patientNdJsonResource, "{\"resourceType\":").Count;
                var notificationList = _metricHandler.NotificationMapping[typeof(ImportJobMetricsNotification)];
                Assert.True(notificationList.Count() >= 1);
                var notification = notificationList.First() as ImportJobMetricsNotification;
                Assert.Equal(JobStatus.Completed.ToString(), notification.Status);
                Assert.NotNull(notification.DataSize);
                Assert.Equal(resourceCount, notification.SucceededCount);
                Assert.Equal(0, notification.FailedCount);
            }
        }

        [Fact]
        public async Task GivenImportResourceWithWrongType_ThenErrorLogShouldBeUploaded()
        {
            _metricHandler?.ResetCount();
            string patientNdJsonResource = Samples.GetNdJson("Import-Patient");
            patientNdJsonResource = Regex.Replace(patientNdJsonResource, "##PatientID##", m => Guid.NewGuid().ToString("N"));
            (Uri location, string etag) = await ImportTestHelper.UploadFileAsync(patientNdJsonResource, _fixture.StorageAccount);

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
                Mode = ImportMode.InitialLoad.ToString(),
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
                Assert.Equal(0, notification.SucceededCount);
                Assert.Equal(resourceCount, notification.FailedCount);
            }
        }

        [Fact]
        public async Task GivenImportTriggeredWithMultipleFiles_ThenDataShouldBeImported()
        {
            _metricHandler?.ResetCount();
            string patientNdJsonResource = Samples.GetNdJson("Import-SinglePatientTemplate");
            string resourceId1 = Guid.NewGuid().ToString("N");
            string patientNdJsonResource1 = patientNdJsonResource.Replace("##PatientID##", resourceId1);
            string resourceId2 = Guid.NewGuid().ToString("N");
            string patientNdJsonResource2 = patientNdJsonResource.Replace("##PatientID##", resourceId2);

            (Uri location1, string _) = await ImportTestHelper.UploadFileAsync(patientNdJsonResource1, _fixture.StorageAccount);
            (Uri location2, string _) = await ImportTestHelper.UploadFileAsync(patientNdJsonResource2, _fixture.StorageAccount);

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
                Mode = ImportMode.InitialLoad.ToString(),
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
                Assert.Equal(resourceCount, notification.SucceededCount);
                Assert.Equal(0, notification.FailedCount);
            }
        }

        [Fact]
        public async Task GivenImportInvalidResource_ThenErrorLogsShouldBeOutput()
        {
            _metricHandler?.ResetCount();
            string patientNdJsonResource = Samples.GetNdJson("Import-InvalidPatient");
            patientNdJsonResource = Regex.Replace(patientNdJsonResource, "##PatientID##", m => Guid.NewGuid().ToString("N"));
            (Uri location, string etag) = await ImportTestHelper.UploadFileAsync(patientNdJsonResource, _fixture.StorageAccount);

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
                Mode = ImportMode.InitialLoad.ToString(),
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
            Assert.Single(result.Error);
            Assert.NotEmpty(result.Request);

            string errorLocation = result.Error.ToArray()[0].Url;
            string[] errorContents = (await ImportTestHelper.DownloadFileAsync(errorLocation, _fixture.StorageAccount)).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            Assert.True(errorContents.Count() >= 1); // when run locally there might be duplicates. no idea why.

            // Only check metric for local tests
            if (_fixture.IsUsingInProcTestServer)
            {
                var resourceCount = Regex.Matches(patientNdJsonResource, "{\"resourceType\":").Count;
                var notificationList = _metricHandler.NotificationMapping[typeof(ImportJobMetricsNotification)];
                Assert.Single(notificationList);
                var notification = notificationList.First() as ImportJobMetricsNotification;
                Assert.Equal(JobStatus.Completed.ToString(), notification.Status);
                Assert.NotNull(notification.DataSize);
                Assert.Equal(resourceCount, notification.SucceededCount);
                Assert.Equal(1, notification.FailedCount);
            }
        }

        [Fact]
        public async Task GivenImportDuplicatedResource_ThenDupResourceShouldBeReported()
        {
            _metricHandler?.ResetCount();
            string patientNdJsonResource = Samples.GetNdJson("Import-DupPatientTemplate");
            string resourceId = Guid.NewGuid().ToString("N");
            patientNdJsonResource = patientNdJsonResource.Replace("##PatientID##", resourceId);
            (Uri location, string etag) = await ImportTestHelper.UploadFileAsync(patientNdJsonResource, _fixture.StorageAccount);

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
                Mode = ImportMode.InitialLoad.ToString(),
            };

            await ImportCheckAsync(request, errorCount: 1);
            //// we have to re-create file as import registration is idempotent
            (Uri location2, string etag2) = await ImportTestHelper.UploadFileAsync(patientNdJsonResource, _fixture.StorageAccount);
            request.Input = new List<InputResource>()
            {
                new InputResource()
                {
                    Url = location2,
                    Etag = etag2,
                    Type = "Patient",
                },
            };
            await ImportCheckAsync(request, errorCount: 2);

            Patient patient = await _client.ReadAsync<Patient>(ResourceType.Patient, resourceId);
            Assert.Equal(resourceId, patient.Id);

            // Only check metric for local tests
            if (_fixture.IsUsingInProcTestServer)
            {
                var notificationList = _metricHandler.NotificationMapping[typeof(ImportJobMetricsNotification)];
                Assert.Equal(2, notificationList.Count);

                var notification1 = notificationList[0] as ImportJobMetricsNotification;
                Assert.Equal(JobStatus.Completed.ToString(), notification1.Status);
                Assert.Equal(1, notification1.SucceededCount);
                Assert.Equal(1, notification1.FailedCount);

                var notification2 = notificationList[1] as ImportJobMetricsNotification;
                Assert.Equal(JobStatus.Completed.ToString(), notification1.Status);
                Assert.Equal(0, notification2.SucceededCount);
                Assert.Equal(2, notification2.FailedCount);
            }
        }

        [Fact]
        public async Task GivenImportWithCancel_ThenTaskShouldBeCanceled()
        {
            _metricHandler?.ResetCount();
            string patientNdJsonResource = Samples.GetNdJson("Import-Patient");
            patientNdJsonResource = Regex.Replace(patientNdJsonResource, "##PatientID##", m => Guid.NewGuid().ToString("N"));
            (Uri location, string etag) = await ImportTestHelper.UploadFileAsync(patientNdJsonResource, _fixture.StorageAccount);

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
                Mode = ImportMode.InitialLoad.ToString(),
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
                Mode = ImportMode.InitialLoad.ToString(),
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
                Assert.Null(notification.SucceededCount);
                Assert.Null(notification.FailedCount);
            }
        }

        [Fact]
        public async Task GivenImportInvalidETag_ThenBadRequestShouldBeReturned()
        {
            _metricHandler?.ResetCount();
            string patientNdJsonResource = Samples.GetNdJson("Import-Patient");
            patientNdJsonResource = Regex.Replace(patientNdJsonResource, "##PatientID##", m => Guid.NewGuid().ToString("N"));
            (Uri location, string etag) = await ImportTestHelper.UploadFileAsync(patientNdJsonResource, _fixture.StorageAccount);

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
                Mode = ImportMode.InitialLoad.ToString(),
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
                Assert.Equal(0, notification.DataSize);
                Assert.Equal(0, notification.SucceededCount);
                Assert.Equal(0, notification.FailedCount);
            }
        }

        [Fact]
        public async Task GivenImportInvalidResourceType_ThenBadRequestShouldBeReturned()
        {
            string patientNdJsonResource = Samples.GetNdJson("Import-Patient");
            patientNdJsonResource = Regex.Replace(patientNdJsonResource, "##PatientID##", m => Guid.NewGuid().ToString("N"));
            (Uri location, string etag) = await ImportTestHelper.UploadFileAsync(patientNdJsonResource, _fixture.StorageAccount);

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
                Mode = ImportMode.InitialLoad.ToString(),
            };

            FhirClientException fhirException = await Assert.ThrowsAsync<FhirClientException>(
                async () => await ImportTestHelper.CreateImportTaskAsync(_client, request));

            Assert.Equal(HttpStatusCode.BadRequest, fhirException.StatusCode);
        }

        private async Task<Uri> ImportCheckAsync(ImportRequest request, TestFhirClient client = null, int? errorCount = null)
        {
            client = client ?? _client;
            Uri checkLocation = await ImportTestHelper.CreateImportTaskAsync(client, request);

            var response = await ImportWaitAsync(checkLocation, client);

            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            ImportJobResult result = JsonConvert.DeserializeObject<ImportJobResult>(await response.Content.ReadAsStringAsync());
            Assert.NotEmpty(result.Output);
            if (errorCount != null && errorCount != 0)
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

        private async Task<HttpResponseMessage> ImportWaitAsync(Uri checkLocation, TestFhirClient client = null)
        {
            client = client ?? _client;
            HttpResponseMessage response;
            while ((response = await client.CheckImportAsync(checkLocation, CancellationToken.None)).StatusCode == HttpStatusCode.Accepted)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
            }

            return response;
        }
    }
}
