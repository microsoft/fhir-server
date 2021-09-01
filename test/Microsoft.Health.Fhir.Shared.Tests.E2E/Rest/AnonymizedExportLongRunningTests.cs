// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.E2E.Rest;
using Microsoft.Health.Test.Utilities;
using Xunit;
using FhirGroup = Hl7.Fhir.Model.Group;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.Rest
{
    [Trait(Traits.Category, Categories.ExportLongRunning)]
    [HttpIntegrationFixtureArgumentSets(DataStore.All, Format.Json)]
    public class AnonymizedExportLongRunningTests : AnonymizedExportTests
    {
        public AnonymizedExportLongRunningTests(ExportTestFixture fixture)
            : base(fixture)
        {
        }

        [Theory]
        [InlineData("")]
        [InlineData("Patient/")]
        public async Task GivenAValidConfigurationWithETag_WhenExportingAnonymizedData_ResourceShouldBeAnonymized(string path)
        {
            MetricHandler?.ResetCount();
            var dateTime = DateTimeOffset.UtcNow;
            var resourceToCreate = Samples.GetDefaultPatient().ToPoco<Patient>();
            resourceToCreate.Id = Guid.NewGuid().ToString();
            await TestFhirClient.UpdateAsync(resourceToCreate);

            (string fileName, string etag) = await UploadConfigurationAsync(RedactResourceIdAnonymizationConfiguration);
            string containerName = Guid.NewGuid().ToString("N");
            Uri contentLocation = await TestFhirClient.AnonymizedExportAsync(fileName, dateTime, containerName, etag, path);
            HttpResponseMessage response = await WaitForCompleteAsync(contentLocation);
            IList<Uri> blobUris = await CheckExportStatus(response);

            IEnumerable<string> dataFromExport = await DownloadBlobAndParse(blobUris);
            FhirJsonParser parser = new FhirJsonParser();

            foreach (string content in dataFromExport)
            {
                Resource result = parser.Parse<Resource>(content);

                Assert.Contains(result.Meta.Security, c => "REDACTED".Equals(c.Code));
            }

            // Only check metric for local tests
            if (IsUsingInProcTestServer)
            {
                Assert.Single(MetricHandler.NotificationMapping[typeof(ExportTaskMetricsNotification)]);
            }
        }

        [Fact]
        public async Task GivenAValidConfigurationWithETag_WhenExportingGroupAnonymizedData_ResourceShouldBeAnonymized()
        {
            MetricHandler?.ResetCount();

            var patientToCreate = Samples.GetDefaultPatient().ToPoco<Patient>();
            var dateTime = DateTimeOffset.UtcNow;
            patientToCreate.Id = Guid.NewGuid().ToString();
            var patientReponse = await TestFhirClient.UpdateAsync(patientToCreate);
            var patientId = patientReponse.Resource.Id;

            var group = new FhirGroup()
            {
                Type = FhirGroup.GroupType.Person,
                Actual = true,
                Id = Guid.NewGuid().ToString(),
                Member = new List<FhirGroup.MemberComponent>()
                {
                    new FhirGroup.MemberComponent()
                    {
                        Entity = new ResourceReference($"{KnownResourceTypes.Patient}/{patientId}"),
                    },
                },
            };
            var groupReponse = await TestFhirClient.UpdateAsync(group);
            var groupId = groupReponse.Resource.Id;

            (string fileName, string etag) = await UploadConfigurationAsync(RedactResourceIdAnonymizationConfiguration);
            string containerName = Guid.NewGuid().ToString("N");
            Uri contentLocation = await TestFhirClient.AnonymizedExportAsync(fileName, dateTime, containerName, etag, $"Group/{groupId}/");
            HttpResponseMessage response = await WaitForCompleteAsync(contentLocation);
            IList<Uri> blobUris = await CheckExportStatus(response);

            IEnumerable<string> dataFromExport = await DownloadBlobAndParse(blobUris);
            FhirJsonParser parser = new FhirJsonParser();

            foreach (string content in dataFromExport)
            {
                Resource result = parser.Parse<Resource>(content);

                Assert.Contains(result.Meta.Security, c => "REDACTED".Equals(c.Code));
            }

            Assert.Equal(2, dataFromExport.Count());

            // Only check metric for local tests
            if (IsUsingInProcTestServer)
            {
                Assert.Single(MetricHandler.NotificationMapping[typeof(ExportTaskMetricsNotification)]);
            }
        }

        [SkippableFact]
        public async Task GivenAValidConfigurationWithETagNoQuotes_WhenExportingAnonymizedData_ResourceShouldBeAnonymized()
        {
            MetricHandler?.ResetCount();
            var dateTime = DateTimeOffset.UtcNow;
            var resourceToCreate = Samples.GetDefaultPatient().ToPoco<Patient>();
            resourceToCreate.Id = Guid.NewGuid().ToString();
            await TestFhirClient.UpdateAsync(resourceToCreate);

            (string fileName, string etag) = await UploadConfigurationAsync(RedactResourceIdAnonymizationConfiguration);
            etag = etag.Substring(1, 17);
            string containerName = Guid.NewGuid().ToString("N");
            Uri contentLocation = await TestFhirClient.AnonymizedExportAsync(fileName, dateTime, containerName, etag);
            HttpResponseMessage response = await WaitForCompleteAsync(contentLocation);
            IList<Uri> blobUris = await CheckExportStatus(response);

            IEnumerable<string> dataFromExport = await DownloadBlobAndParse(blobUris);
            FhirJsonParser parser = new FhirJsonParser();

            foreach (string content in dataFromExport)
            {
                Resource result = parser.Parse<Resource>(content);

                Assert.Contains(result.Meta.Security, c => "REDACTED".Equals(c.Code));
            }

            // Only check metric for local tests
            if (IsUsingInProcTestServer)
            {
                Assert.Single(MetricHandler.NotificationMapping[typeof(ExportTaskMetricsNotification)]);
            }
        }

        [SkippableFact]
        public async Task GivenAValidConfigurationWithoutETag_WhenExportingAnonymizedData_ResourceShouldBeAnonymized()
        {
            MetricHandler?.ResetCount();

            var resourceToCreate = Samples.GetDefaultPatient().ToPoco<Patient>();
            resourceToCreate.Id = Guid.NewGuid().ToString();
            var dateTime = DateTimeOffset.UtcNow;
            await TestFhirClient.UpdateAsync(resourceToCreate);

            (string fileName, string _) = await UploadConfigurationAsync(RedactResourceIdAnonymizationConfiguration);

            string containerName = Guid.NewGuid().ToString("N");
            Uri contentLocation = await TestFhirClient.AnonymizedExportAsync(fileName, dateTime, containerName);
            HttpResponseMessage response = await WaitForCompleteAsync(contentLocation);
            IList<Uri> blobUris = await CheckExportStatus(response);

            IEnumerable<string> dataFromExport = await DownloadBlobAndParse(blobUris);
            FhirJsonParser parser = new FhirJsonParser();

            foreach (string content in dataFromExport)
            {
                Resource result = parser.Parse<Resource>(content);

                Assert.Contains(result.Meta.Security, c => "REDACTED".Equals(c.Code));
            }

            // Only check metric for local tests
            if (IsUsingInProcTestServer)
            {
                Assert.Single(MetricHandler.NotificationMapping[typeof(ExportTaskMetricsNotification)]);
            }
        }
    }
}
