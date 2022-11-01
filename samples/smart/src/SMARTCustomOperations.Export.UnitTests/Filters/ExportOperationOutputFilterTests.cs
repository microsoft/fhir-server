// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AzureHealth.DataServices.Clients.Headers;
using Microsoft.AzureHealth.DataServices.Pipelines;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SMARTCustomOperations.Export.Configuration;
using SMARTCustomOperations.Export.Filters;

namespace SMARTCustomOperations.Export.UnitTests.Filters
{
    public class ExportOperationOutputFilterTests
    {
        private static ExportCustomOperationsConfig _config = new()
        {
            ApiManagementHostName = "apim-name.azure-api.net",
            ApiManagementFhirPrefex = "smart",
            FhirServerUrl = "https://workspace-fhir.fhir.azurehealthcareapis.com",
            ExportStorageAccountUrl = "https://account.blob.core.windows.net",
        };

        private static ILogger<ExportOperationOutputFilter> _logger = Substitute.For<ILogger<ExportOperationOutputFilter>>();

        [Fact]
        public async Task GivenAGroupExportOperation_WhenOutputIsProcessed_ContentLocationHeaderIsSetCorrectly()
        {
            string exportId = "42";
            string origContentLocation = $"{_config.FhirServerUrl}/_operations/export/{exportId}";

            OperationContext context = new();
            context.StatusCode = System.Net.HttpStatusCode.Accepted;
            context.Properties["PipelineType"] = ExportOperationType.GroupExport.ToString();
            context.Headers.Add(new HeaderNameValuePair("Content-Location", origContentLocation, CustomHeaderType.ResponseStatic));

            ExportOperationOutputFilter filter = new(_logger, _config);
            OperationContext newContext = await filter.ExecuteAsync(context);

            var expectedContentLocaton = $"https://{_config.ApiManagementHostName}/{_config.ApiManagementFhirPrefex}/_operations/export/{exportId}";
            Assert.Equal(expectedContentLocaton, newContext.Headers.Single(x => x.Name == "Content-Location").Value);
        }
    }
}
