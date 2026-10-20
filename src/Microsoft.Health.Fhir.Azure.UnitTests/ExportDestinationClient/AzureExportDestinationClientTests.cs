// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Core.Pipeline;
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

        [Fact]
        public async Task GivenCommitIsAppliedButResponseIsLost_WhenCommittingFile_ThenCommitSucceedsAndFileIsComplete()
        {
            FakeBlobStorageHandler storage = await ConnectToFakeStorageAsync();
            WriteLines("Patient-1-1.ndjson", 3);
            storage.CommitResponsesToLose = 1;

            Uri uri = _exportDestinationClient.CommitFile("Patient-1-1.ndjson");

            Assert.EndsWith("/export/Patient-1-1.ndjson", uri.AbsolutePath);
            Assert.Equal(ExpectedContent(3), storage.GetBlobContent("Patient-1-1.ndjson"));
        }

        [Fact]
        public async Task GivenFileIsWritten_WhenCommittingFile_ThenCommitIsSentWithoutIfMatchCondition()
        {
            FakeBlobStorageHandler storage = await ConnectToFakeStorageAsync();
            WriteLines("Patient-1-1.ndjson", 3);

            _exportDestinationClient.CommitFile("Patient-1-1.ndjson");

            Assert.NotEmpty(storage.CommitIfMatchHeaders);
            Assert.All(storage.CommitIfMatchHeaders, Assert.Null);
        }

        [Fact]
        public async Task GivenFileLargerThanOneBlock_WhenCommittingFile_ThenAllBlocksAreCommittedInOrder()
        {
            FakeBlobStorageHandler storage = await ConnectToFakeStorageAsync();
            WriteLines("Observation-1-1.ndjson", 10_000, lineLength: 1_000);

            _exportDestinationClient.CommitFile("Observation-1-1.ndjson");

            Assert.True(storage.StagedBlockCount >= 3);
            Assert.Equal(ExpectedContent(10_000, lineLength: 1_000), storage.GetBlobContent("Observation-1-1.ndjson"));
        }

        [Fact]
        public async Task GivenCommitIsRejected_WhenCommittingFile_ThenDestinationConnectionExceptionIsThrownAndLaterCommitWritesFileOnce()
        {
            FakeBlobStorageHandler storage = await ConnectToFakeStorageAsync();
            WriteLines("Patient-1-1.ndjson", 3);
            storage.RejectCommitsWith = HttpStatusCode.Forbidden;

            var exception = Assert.Throws<DestinationConnectionException>(() => _exportDestinationClient.CommitFile("Patient-1-1.ndjson"));
            Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
            Assert.Null(storage.GetBlobContent("Patient-1-1.ndjson"));

            storage.RejectCommitsWith = null;
            _exportDestinationClient.CommitFile("Patient-1-1.ndjson");

            Assert.Equal(ExpectedContent(3), storage.GetBlobContent("Patient-1-1.ndjson"));
        }

        [Fact]
        public async Task GivenMultipleFiles_WhenCommitting_ThenAllFilesAreCommittedAndReturned()
        {
            FakeBlobStorageHandler storage = await ConnectToFakeStorageAsync();
            WriteLines("Patient-1-1.ndjson", 2);
            WriteLines("Observation-1-1.ndjson", 4);

            var result = _exportDestinationClient.Commit();

            Assert.Equal(new[] { "Observation-1-1.ndjson", "Patient-1-1.ndjson" }, result.Keys.OrderBy(k => k, StringComparer.Ordinal));
            Assert.Equal(ExpectedContent(2), storage.GetBlobContent("Patient-1-1.ndjson"));
            Assert.Equal(ExpectedContent(4), storage.GetBlobContent("Observation-1-1.ndjson"));
            Assert.Empty(_exportDestinationClient.Commit());
        }

        [Fact]
        public async Task GivenOneFileFailsInBatchCommit_WhenMoreDataIsWrittenAndCommitted_ThenEarlierCommittedFileKeepsAllData()
        {
            FakeBlobStorageHandler storage = await ConnectToFakeStorageAsync();
            WriteLines("Patient-1-1.ndjson", 2);
            WriteLines("Observation-1-1.ndjson", 2);
            storage.RejectCommitsWith = HttpStatusCode.ServiceUnavailable;
            storage.RejectCommitsForBlob = "Observation-1-1.ndjson";

            Assert.Throws<DestinationConnectionException>(() => _exportDestinationClient.Commit());
            Assert.Equal(ExpectedContent(2), storage.GetBlobContent("Patient-1-1.ndjson"));

            storage.RejectCommitsWith = null;
            _exportDestinationClient.WriteFilePart("Patient-1-1.ndjson", Line(2, 10));
            _exportDestinationClient.WriteFilePart("Observation-1-1.ndjson", Line(2, 10));
            var result = _exportDestinationClient.Commit();

            Assert.Equal(2, result.Count);
            Assert.Equal(ExpectedContent(3), storage.GetBlobContent("Patient-1-1.ndjson"));
            Assert.Equal(ExpectedContent(3), storage.GetBlobContent("Observation-1-1.ndjson"));
        }

        private async Task<FakeBlobStorageHandler> ConnectToFakeStorageAsync()
        {
            var storage = new FakeBlobStorageHandler();
            var options = new BlobClientOptions
            {
                Transport = new HttpClientTransport(new HttpClient(storage)),
            };
            options.Retry.Mode = RetryMode.Fixed;
            options.Retry.Delay = TimeSpan.FromMilliseconds(1);
            options.Retry.MaxRetries = 3;

            var blobServiceClient = new BlobServiceClient(new Uri("https://fakeaccount.blob.core.windows.net"), options);
            _exportClientInitializer.GetAuthorizedClient(Arg.Any<ExportJobConfiguration>()).Returns(blobServiceClient);

            await _exportDestinationClient.ConnectAsync(CancellationToken.None, "export");
            return storage;
        }

        private void WriteLines(string fileName, int count, int lineLength = 10)
        {
            for (int i = 0; i < count; i++)
            {
                _exportDestinationClient.WriteFilePart(fileName, Line(i, lineLength));
            }
        }

        private static string ExpectedContent(int count, int lineLength = 10)
        {
            var builder = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                builder.Append(Line(i, lineLength)).Append(Environment.NewLine);
            }

            return builder.ToString();
        }

        private static string Line(int index, int length)
        {
            return $"{{\"id\":\"{index}\",\"pad\":\"{new string('x', length)}\"}}";
        }
    }
}
