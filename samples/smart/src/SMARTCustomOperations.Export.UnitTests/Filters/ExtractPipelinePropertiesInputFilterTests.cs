// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net.Http.Headers;
using Microsoft.AzureHealth.DataServices.Pipelines;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SMARTCustomOperations.Export.Configuration;
using SMARTCustomOperations.Export.Filters;

namespace SMARTCustomOperations.Export.UnitTests.Filters
{
    public class ExtractPipelinePropertiesInputFilterTests
    {
        private const string _prefix = "smart";

        private const string _fhirBaseUrl = "https://workspace-fhir.fhir.azurehealthcareapis.com";

        private static ILogger<ExtractPipelinePropertiesInputFilter> _logger = Substitute.For<ILogger<ExtractPipelinePropertiesInputFilter>>();

        [Fact]
        public void GivenAGroupExportOperation_WhenSavingPipelineTypeToProperties_PropertiesAreCorrect()
        {
            string id = Guid.NewGuid().ToString();
            OperationContext context = new();
            context.Request = new HttpRequestMessage(HttpMethod.Get, _fhirBaseUrl + $"/{_prefix}/Group/{id}/$export?_container=bad");

            var filter = new ExtractPipelinePropertiesInputFilter(_logger);
            OperationContext newContext = filter.SavePipelineTypeToProperties(context);

            Assert.Equal(ExportOperationType.GroupExport.ToString(), newContext.Properties["PipelineType"]);
            Assert.Equal(id, newContext.Properties["GroupId"]);
        }

        [Fact]
        public void GivenAExportCheckOperation_WhenSavingPipelineTypeToProperties_PropertiesAreCorrect()
        {
            string id = "42";
            OperationContext context = new();
            context.Request = new HttpRequestMessage(HttpMethod.Get, _fhirBaseUrl + $"/{_prefix}/_operations/export/{id}");

            var filter = new ExtractPipelinePropertiesInputFilter(_logger);
            OperationContext newContext = filter.SavePipelineTypeToProperties(context);

            Assert.Equal(ExportOperationType.ExportCheck.ToString(), newContext.Properties["PipelineType"]);
            Assert.Equal(id, newContext.Properties["ExportOperationId"]);
        }

        [Fact]
        public void GivenAGetExportFileOperation_WhenSavingPipelineTypeToProperties_PropertiesAreCorrect()
        {
            string containerName = Guid.NewGuid().ToString();
            string restOfPath = "DateTimeFolder/file.ndjson";
            OperationContext context = new();
            context.Request = new HttpRequestMessage(HttpMethod.Get, _fhirBaseUrl + $"/{_prefix}/_export/{containerName}/{restOfPath}");

            var filter = new ExtractPipelinePropertiesInputFilter(_logger);
            OperationContext newContext = filter.SavePipelineTypeToProperties(context);

            Assert.Equal(ExportOperationType.GetExportFile.ToString(), newContext.Properties["PipelineType"]);
            Assert.Equal(containerName, newContext.Properties["ContainerName"]);
            Assert.Equal(restOfPath, newContext.Properties["RestOfPath"]);
        }

        [Fact]
        public void GivenARequestWithOidClaim_WhenSavingPipelineTypeToProperties_PropertiesAreCorrect()
        {
            string oid = "1234567890";
            string token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJvaWQiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ.Sgs5gDXkwhnM9bPxSSs1v4_TJaBy8ZxzftGOzmha-EQ";
            OperationContext context = new();

            var message = new HttpRequestMessage();
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            context.Request = message;

            var filter = new ExtractPipelinePropertiesInputFilter(_logger);
            OperationContext newContext = filter.ExtractOidClaimToProperties(context);

            Assert.Equal(token, newContext.Properties["token"]);
            Assert.Equal(oid, newContext.Properties["oid"]);
        }
    }
}
