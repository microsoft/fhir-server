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
using Azure;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
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
        private const int LargeFileLineCount = 10_000;
        private const int LargeFileLineLength = 1_000;

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
        public async Task GivenAuthenticationFails_WhenConnectingToStorage_ThenDestinationConnectionIsForbidden()
        {
            // Arrange
            var blobClient = Substitute.For<BlobServiceClient>();
            var container = Substitute.For<BlobContainerClient>();
            _exportClientInitializer.GetAuthorizedClient(Arg.Any<ExportJobConfiguration>()).Returns(blobClient);
            blobClient.GetBlobContainerClient(Arg.Any<string>()).Returns(container);
            container.CreateIfNotExistsAsync(cancellationToken: Arg.Any<CancellationToken>())
                .Returns(Task.FromException<Response<BlobContainerInfo>>(new AuthenticationFailedException("Managed identity authentication failed.")));

            // Act
            var exception = await Assert.ThrowsAsync<DestinationConnectionException>(() => _exportDestinationClient.ConnectAsync(CancellationToken.None, "export"));

            // Assert
            Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
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
        public async Task GivenFileLargerThanOneBlock_WhenCommittingFile_ThenFileIsCommittedWithIfMatchAndIsNotReadBack()
        {
            FakeBlobStorageHandler storage = await ConnectToFakeStorageAsync();
            WriteLines("Observation-1-1.ndjson", LargeFileLineCount, LargeFileLineLength);

            Uri uri = _exportDestinationClient.CommitFile("Observation-1-1.ndjson");

            Assert.EndsWith("/export/Observation-1-1.ndjson", uri.AbsolutePath);
            Assert.True(storage.StagedBlockCount >= 3);
            Assert.Equal(ExpectedContent(LargeFileLineCount, LargeFileLineLength), storage.GetBlobContent("Observation-1-1.ndjson"));

            // The normal path is unchanged: every commit keeps the SDK If-Match condition, and nothing reads the blob.
            Assert.All(storage.CommitIfMatchHeaders, Assert.NotNull);
            Assert.DoesNotContain(HttpStatusCode.PreconditionFailed, storage.CommitResponseStatuses);
            Assert.Equal(0, storage.GetPropertiesRequestCount);
        }

        [Fact]
        public async Task GivenCommitIsAppliedButResponseIsLost_WhenCommittingFile_ThenCommitSucceedsAndFileContainsEachLineOnce()
        {
            FakeBlobStorageHandler storage = await ConnectToFakeStorageAsync();
            WriteLines("Observation-1-1.ndjson", LargeFileLineCount, LargeFileLineLength);
            storage.CommitsWithLostResponse.Add(1);

            Uri uri = _exportDestinationClient.CommitFile("Observation-1-1.ndjson");

            Assert.EndsWith("/export/Observation-1-1.ndjson", uri.AbsolutePath);

            // The SDK retried the applied commit with the old ETag and got 412, as in production.
            Assert.Equal(HttpStatusCode.InternalServerError, storage.CommitResponseStatuses[0]);
            Assert.Equal(HttpStatusCode.PreconditionFailed, storage.CommitResponseStatuses[1]);

            // The size check ran once, and the file holds every line exactly once.
            Assert.Equal(1, storage.GetPropertiesRequestCount);
            Assert.Equal(ExpectedContent(LargeFileLineCount, LargeFileLineLength), storage.GetBlobContent("Observation-1-1.ndjson"));

            // The writer was released without another commit, which would fail again with the old ETag.
            int commitCount = storage.CommitIfMatchHeaders.Count;
            Assert.Empty(_exportDestinationClient.Commit());
            Assert.Equal(commitCount, storage.CommitIfMatchHeaders.Count);
        }

        [Fact]
        public async Task GivenCommitFromCloseIsAppliedButResponseIsLost_WhenCommittingFile_ThenCommitSucceeds()
        {
            FakeBlobStorageHandler storage = await ConnectToFakeStorageAsync();
            WriteLines("Patient-1-1.ndjson", 3);

            // Commit 1 comes from Flush and succeeds. Commit 2 comes from Close; storage applies it but the response is lost.
            storage.CommitsWithLostResponse.Add(2);

            Uri uri = _exportDestinationClient.CommitFile("Patient-1-1.ndjson");

            Assert.EndsWith("/export/Patient-1-1.ndjson", uri.AbsolutePath);
            Assert.Contains(HttpStatusCode.PreconditionFailed, storage.CommitResponseStatuses);
            Assert.Equal(1, storage.GetPropertiesRequestCount);
            Assert.Equal(ExpectedContent(3), storage.GetBlobContent("Patient-1-1.ndjson"));
        }

        [Fact]
        public async Task GivenCommitResponseIsLostForOneFile_WhenCommittingAllFiles_ThenAllFilesAreCommittedAndReturned()
        {
            FakeBlobStorageHandler storage = await ConnectToFakeStorageAsync();
            WriteLines("Patient-1-1.ndjson", 2);
            WriteLines("Observation-1-1.ndjson", 4);
            storage.CommitsWithLostResponse.Add(1);

            var result = _exportDestinationClient.Commit();

            Assert.Equal(new[] { "Observation-1-1.ndjson", "Patient-1-1.ndjson" }, result.Keys.OrderBy(k => k, StringComparer.Ordinal));
            Assert.Equal(ExpectedContent(2), storage.GetBlobContent("Patient-1-1.ndjson"));
            Assert.Equal(ExpectedContent(4), storage.GetBlobContent("Observation-1-1.ndjson"));
            Assert.Equal(1, storage.GetPropertiesRequestCount);
            Assert.Empty(_exportDestinationClient.Commit());
        }

        [Fact]
        public async Task GivenFileIsChangedByAnotherWriter_WhenCommittingFile_ThenConditionNotMetIsReturnedAndOtherContentIsKept()
        {
            FakeBlobStorageHandler storage = await ConnectToFakeStorageAsync();
            WriteLines("Patient-1-1.ndjson", 3);
            storage.OverwriteBlob("Patient-1-1.ndjson", "other writer");

            var exception = Assert.Throws<DestinationConnectionException>(() => _exportDestinationClient.CommitFile("Patient-1-1.ndjson"));

            // A genuine conflict still fails: the size check runs on both attempts and doesn't match the written data.
            Assert.Equal(HttpStatusCode.PreconditionFailed, exception.StatusCode);
            Assert.Equal(2, storage.GetPropertiesRequestCount);
            Assert.Equal("other writer", storage.GetBlobContent("Patient-1-1.ndjson"));
        }

        [Fact]
        public async Task GivenCommitResponseIsLostAndFileCannotBeRead_WhenCommittingFile_ThenConditionNotMetIsReturned()
        {
            FakeBlobStorageHandler storage = await ConnectToFakeStorageAsync();
            WriteLines("Patient-1-1.ndjson", 3);
            storage.CommitsWithLostResponse.Add(1);
            storage.FailGetPropertiesWith = HttpStatusCode.ServiceUnavailable;

            var exception = Assert.Throws<DestinationConnectionException>(() => _exportDestinationClient.CommitFile("Patient-1-1.ndjson"));

            // The commit can't be verified, so the original behavior is kept.
            Assert.Equal(HttpStatusCode.PreconditionFailed, exception.StatusCode);
            Assert.True(storage.GetPropertiesRequestCount > 0);
        }

        [Fact]
        public async Task GivenCommitResponseIsLostAndPropertiesRequestFailsAtTransportLevel_WhenCommittingFile_ThenConditionNotMetIsReturned()
        {
            FakeBlobStorageHandler storage = await ConnectToFakeStorageAsync();
            WriteLines("Patient-1-1.ndjson", 3);
            storage.CommitsWithLostResponse.Add(1);
            storage.FailGetPropertiesTransport = true;

            var exception = Assert.Throws<DestinationConnectionException>(() => _exportDestinationClient.CommitFile("Patient-1-1.ndjson"));

            // The SDK retried the properties request and threw an AggregateException; the original 412 is still reported.
            Assert.Equal(HttpStatusCode.PreconditionFailed, exception.StatusCode);
            Assert.True(storage.GetPropertiesRequestCount > 1);
        }

        [Fact]
        public async Task GivenCommitFailsWithOtherError_WhenCommittingFile_ThenErrorIsReturnedAndFileIsNotRead()
        {
            FakeBlobStorageHandler storage = await ConnectToFakeStorageAsync();
            WriteLines("Patient-1-1.ndjson", 3);
            storage.RejectCommitsWith = HttpStatusCode.Forbidden;

            var exception = Assert.Throws<DestinationConnectionException>(() => _exportDestinationClient.CommitFile("Patient-1-1.ndjson"));

            Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
            Assert.Equal(0, storage.GetPropertiesRequestCount);
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
            Assert.Equal(0, storage.GetPropertiesRequestCount);
            Assert.Empty(_exportDestinationClient.Commit());
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
