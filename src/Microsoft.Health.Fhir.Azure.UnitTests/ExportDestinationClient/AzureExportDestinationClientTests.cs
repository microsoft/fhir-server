// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Azure.ExportDestinationClient;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Azure.UnitTests.ExportDestinationClient
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class AzureExportDestinationClientTests
    {
        private IExportClientInitializer<BlobServiceClient> _exportClientInitializer;
        private ExportJobConfiguration _exportJobConfiguration;
        private ILogger<AzureExportDestinationClient> _logger;

        private AzureExportDestinationClient _exportDestinationClient;

        public AzureExportDestinationClientTests()
        {
            _exportClientInitializer = Substitute.For<IExportClientInitializer<BlobServiceClient>>();
            _logger = Substitute.For<ILogger<AzureExportDestinationClient>>();

            _exportJobConfiguration = new ExportJobConfiguration();
            IOptions<ExportJobConfiguration> optionsExportConfig = Substitute.For<IOptions<ExportJobConfiguration>>();
            optionsExportConfig.Value.Returns(_exportJobConfiguration);

            _exportDestinationClient = new AzureExportDestinationClient(_exportClientInitializer, optionsExportConfig, _logger);
        }

        [Fact]
        public void GivenNullExportClientInitializer_WhenCreatingClient_ThenThrowsArgumentNullException()
        {
            // Arrange
            var config = Options.Create(new ExportJobConfiguration());
            var logger = Substitute.For<ILogger<AzureExportDestinationClient>>();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                new AzureExportDestinationClient(null, config, logger));
        }

        [Fact]
        public void GivenNullConfiguration_WhenCreatingClient_ThenThrowsArgumentNullException()
        {
            // Arrange
            var initializer = Substitute.For<IExportClientInitializer<BlobServiceClient>>();
            var logger = Substitute.For<ILogger<AzureExportDestinationClient>>();

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                new AzureExportDestinationClient(initializer, null, logger));
        }

        [Fact]
        public void GivenNullLogger_WhenCreatingClient_ThenThrowsArgumentNullException()
        {
            // Arrange
            var initializer = Substitute.For<IExportClientInitializer<BlobServiceClient>>();
            var config = Options.Create(new ExportJobConfiguration());

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() =>
                new AzureExportDestinationClient(initializer, config, null));
        }

        [Fact]
        public async Task GivenUnableToInitializeExportClient_WhenConnectAsync_ThenDestinationConnectionExceptionIsThrown()
        {
            // Arrange
            string message = "Can't initialize client";
            HttpStatusCode statusCode = HttpStatusCode.BadRequest;

            _exportClientInitializer.GetAuthorizedClient(Arg.Any<ExportJobConfiguration>()).Returns<BlobServiceClient>(x => throw new ExportClientInitializerException(message, statusCode));

            // Act
            var exception = await Assert.ThrowsAsync<DestinationConnectionException>(() => _exportDestinationClient.ConnectAsync(CancellationToken.None));

            // Assert
            Assert.Contains(message, exception.Message);
            Assert.Equal(statusCode, exception.StatusCode);
        }

        [Fact]
        public async Task GivenExportClientInitializerThrowsException_WhenConnectAsyncWithConfiguration_ThenDestinationConnectionExceptionIsThrown()
        {
            // Arrange
            string message = "Can't initialize client";
            HttpStatusCode statusCode = HttpStatusCode.Unauthorized;
            var customConfig = new ExportJobConfiguration { StorageAccountConnection = "custom-connection" };

            _exportClientInitializer.GetAuthorizedClient(Arg.Any<ExportJobConfiguration>()).Returns<BlobServiceClient>(x => throw new ExportClientInitializerException(message, statusCode));

            // Act
            var exception = await Assert.ThrowsAsync<DestinationConnectionException>(() =>
                _exportDestinationClient.ConnectAsync(customConfig, CancellationToken.None));

            // Assert
            Assert.Contains(message, exception.Message);
            Assert.Equal(statusCode, exception.StatusCode);
        }

        [Fact]
        public void GivenNullFileName_WhenWritingFilePart_ThenThrowsArgumentNullException()
        {
            // Arrange & Act & Assert
            Assert.Throws<ArgumentNullException>(() => _exportDestinationClient.WriteFilePart(null, "data"));
        }

        [Fact]
        public void GivenNullData_WhenWritingFilePart_ThenThrowsArgumentNullException()
        {
            // Arrange & Act & Assert
            Assert.Throws<ArgumentNullException>(() => _exportDestinationClient.WriteFilePart("file.ndjson", null));
        }

        [Fact]
        public void GivenClientNotConnected_WhenWritingFilePart_ThenThrowsDestinationConnectionException()
        {
            // Arrange & Act & Assert
            var exception = Assert.Throws<DestinationConnectionException>(() =>
                _exportDestinationClient.WriteFilePart("file.ndjson", "data"));

            Assert.Equal(HttpStatusCode.InternalServerError, exception.StatusCode);
        }

        [Fact]
        public void GivenNonExistentFile_WhenCommittingFile_ThenThrowsArgumentException()
        {
            // Arrange & Act & Assert
            Assert.Throws<ArgumentException>(() => _exportDestinationClient.CommitFile("nonexistent.ndjson"));
        }

        [Fact]
        public void GivenClientNotConnected_WhenCommitting_ThenReturnsEmptyDictionary()
        {
            // Arrange & Act
            var result = _exportDestinationClient.Commit();

            // Assert
            Assert.NotNull(result);
            Assert.Empty(result);
        }
    }
}
