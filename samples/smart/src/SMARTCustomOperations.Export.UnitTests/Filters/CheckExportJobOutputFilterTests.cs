// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using Microsoft.AzureHealth.DataServices.Filters;
using Microsoft.AzureHealth.DataServices.Pipelines;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using NSubstitute;
using SMARTCustomOperations.Export.Configuration;
using SMARTCustomOperations.Export.Filters;

namespace SMARTCustomOperations.Export.UnitTests.Filters
{
    public class CheckExportJobOutputFilterTests
    {
        private static ExportCustomOperationsConfig _config = new()
        {
            ApiManagementHostName = "apim-name.azure-api.net",
            ApiManagementFhirPrefex = "smart",
            FhirServerUrl = "https://workspace-fhir.fhir.azurehealthcareapis.com",
            ExportStorageAccountUrl = "https://account.blob.core.windows.net",
        };

        private static ILogger<CheckExportJobOutputFilter> _logger = Substitute.For<ILogger<CheckExportJobOutputFilter>>();

        [Fact]
        public async Task GivenAGetExportCheckOperation_WhenAccessingAllowedFile_FileObjectIsReturnedCorrectly()
        {
            string groupId = Guid.NewGuid().ToString();
            string oid = Guid.NewGuid().ToString();
            string restOfPath = "DateTimeFolder/filename.ndjson";

            OperationContext context = new();
            context.Request = new HttpRequestMessage(HttpMethod.Get, $"https://{_config.ApiManagementHostName}/{_config.ApiManagementFhirPrefex}/Group/{groupId}/$export");
            context.Properties["PipelineType"] = ExportOperationType.ExportCheck.ToString();
            context.Properties["oid"] = oid;

            JObject payload = new();
            payload["requireAccessToken"] = false;

            JObject urlOne = new JObject();
            urlOne["url"] = $"{_config.ExportStorageAccountUrl}/{oid}/{restOfPath}";
            payload["output"] = new JArray() { urlOne };
            context.ContentString = payload.ToString();

            CheckExportJobOutputFilter filter = new(_logger, _config);
            OperationContext newContext = await filter.ExecuteAsync(context);

            JObject newContextPayload = JObject.Parse(newContext.ContentString);
            Assert.Equal(true, newContextPayload["requireAccessToken"]);
            string expectedUrlOne = $"https://{_config.ApiManagementHostName}/{_config.ApiManagementFhirPrefex}/_export/{oid}/{restOfPath}";
            Assert.Equal(expectedUrlOne, newContextPayload["output"]![0]!["url"]!.ToString());
        }

        [Fact]
        public async Task GivenAGetExportCheckOperation_WhenWrongUserIsAccessing_FilterErrors()
        {
            string groupId = Guid.NewGuid().ToString();
            string oid = Guid.NewGuid().ToString();
            string containerName = Guid.NewGuid().ToString();
            string restOfPath = "DateTimeFolder/filename.ndjson";

            OperationContext context = new();
            context.Request = new HttpRequestMessage(HttpMethod.Get, $"https://{_config.ApiManagementHostName}/{_config.ApiManagementFhirPrefex}/Group/{groupId}/$export");
            context.Properties["PipelineType"] = ExportOperationType.ExportCheck.ToString();
            context.Properties["oid"] = oid;

            JObject payload = new();
            payload["requireAccessToken"] = false;

            JObject urlOne = new JObject();
            urlOne["url"] = $"{_config.ExportStorageAccountUrl}/{containerName}/{restOfPath}";
            payload["output"] = new JArray() { urlOne };
            context.ContentString = payload.ToString();

            CheckExportJobOutputFilter filter = new(_logger, _config);
            bool trigger = false;
            filter.OnFilterError += (object? sender, FilterErrorEventArgs args) =>
            {
                trigger = true;
                Assert.Equal(nameof(CheckExportJobOutputFilter), args.Name);
                Assert.Equal(filter.Id, args.Id);
                Assert.True(args.IsFatal);
                Assert.Equal(HttpStatusCode.Unauthorized, args.Code);
            };

            OperationContext newContext = await filter.ExecuteAsync(context);

            // Ensure error was triggered
            Assert.True(trigger);
        }
    }
}
