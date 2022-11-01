// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AzureHealth.DataServices.Clients;
using Microsoft.AzureHealth.DataServices.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using SMARTCustomOperations.Export.Bindings;
using SMARTCustomOperations.Export.Configuration;

namespace SMARTCustomOperations.Export.UnitTests.Bindings
{
    public class ExportBindingTests
    {
        private static ExportBindingOptions _config = new ExportBindingOptions()
        {
            FhirServerEndpoint = "https://workspace-fhir.fhir.azurehealthcareapis.com",
            StorageEndpoint = "https://account.blob.core.windows.net",
        };

        private static ILogger<ExportBinding> _logger = Substitute.For<ILogger<ExportBinding>>();

        private static IAuthenticator _auth = Substitute.For<IAuthenticator>();

        [Fact]
        public async Task GivenAGroupExportOperation_WhenCreatingRestRequestBuilder_BuilderIsProperlyFormed()
        {
            ExportBinding binding = new ExportBinding(_logger, Options.Create(_config), _auth);
            string groupId = Guid.NewGuid().ToString();
            string oid = Guid.NewGuid().ToString();
            string token = Guid.NewGuid().ToString();
            Uri requestUri = new Uri($"https://apim-name.azure-api.net/smart/Group/{groupId}/$export?_container=bad");

            RestRequestBuilder builder = await binding.GetBuilderForOperationType(ExportOperationType.GroupExport, requestUri, oid, token);

            Assert.Equal("GET", builder.Method);
            Assert.Equal(_config.FhirServerEndpoint, builder.BaseUrl);
            Assert.Equal(token, builder.SecurityToken);
            Assert.Equal($"/Group/{groupId}/$export", builder.Path);
            Assert.Equal($"_container={oid}", builder.QueryString);
            Assert.Equal("application/json", builder.ContentType);
        }

        [Fact]
        public async Task GivenACheckExportStatusOperation_WhenCreatingRestRequestBuilder_BuilderIsProperlyFormed()
        {
            ExportBinding binding = new ExportBinding(_logger, Options.Create(_config), _auth);
            string exportId = "42";
            string oid = Guid.NewGuid().ToString();
            string token = Guid.NewGuid().ToString();
            Uri requestUri = new Uri($"https://apim-name.azure-api.net/smart/_operations/export/{exportId}");

            RestRequestBuilder builder = await binding.GetBuilderForOperationType(ExportOperationType.ExportCheck, requestUri, oid, token);

            Assert.Equal("GET", builder.Method);
            Assert.Equal(_config.FhirServerEndpoint, builder.BaseUrl);
            Assert.Equal(token, builder.SecurityToken);
            Assert.Equal($"/_operations/export/{exportId}", builder.Path);
            Assert.Equal(string.Empty, builder.QueryString);
            Assert.Equal("application/json", builder.ContentType);
        }

        [Fact]
        public async Task GivenAGetExportFileOperation_WhenCreatingRestRequestBuilder_BuilderIsProperlyFormed()
        {
            string storageToken = Guid.NewGuid().ToString();
            var auth = Substitute.For<IAuthenticator>();
            auth.AcquireTokenForClientAsync(_config.StorageEndpoint).Returns(storageToken);

            ExportBinding binding = new ExportBinding(_logger, Options.Create(_config), auth);
            string oid = Guid.NewGuid().ToString();
            string token = Guid.NewGuid().ToString();
            Uri requestUri = new Uri($"https://apim-name.azure-api.net/smart/_export/{oid}/DateTimeFolder/filename.ndjson");

            RestRequestBuilder builder = await binding.GetBuilderForOperationType(ExportOperationType.GetExportFile, requestUri, oid, token);

            Assert.Equal("GET", builder.Method);
            Assert.Equal(_config.StorageEndpoint, builder.BaseUrl);
            Assert.Equal(storageToken, builder.SecurityToken);
            Assert.Equal($"/{oid}/DateTimeFolder/filename.ndjson", builder.Path);
            Assert.Equal(string.Empty, builder.QueryString);
            Assert.Equal("application/fhir+ndjson", builder.ContentType);
        }
    }
}
