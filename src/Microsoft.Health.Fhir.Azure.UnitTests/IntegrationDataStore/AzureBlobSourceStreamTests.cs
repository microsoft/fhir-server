// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Azure.IntegrationDataStore;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.Health.Fhir.Azure.UnitTests.IntegrationDataStore
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class AzureBlobSourceStreamTests
    {
        private readonly BlobClient _blobClient = Substitute.For<BlobClient>();
        private readonly NullLogger<AzureBlobSourceStreamTests> _logger = new NullLogger<AzureBlobSourceStreamTests>();

        [Fact]
        public void GivenNullBlobClientFactory_WhenCreatingStream_ThenThrowsArgumentNullException()
        {
            // Arrange & Act & Assert
            Assert.Throws<ArgumentNullException>(() => new AzureBlobSourceStream(null, 0, _logger));
        }

        [Fact]
        public void GivenNullLogger_WhenCreatingStream_ThenThrowsArgumentNullException()
        {
            // Arrange
            Func<Task<BlobClient>> blobClientFactory = () => Task.FromResult(_blobClient);

            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => new AzureBlobSourceStream(blobClientFactory, 0, null));
        }

        [Fact]
        public void GivenValidParameters_WhenCreatingStream_ThenStreamIsCreated()
        {
            // Arrange
            Func<Task<BlobClient>> blobClientFactory = () => Task.FromResult(_blobClient);

            // Act
            using var stream = new AzureBlobSourceStream(blobClientFactory, 0, _logger);

            // Assert
            Assert.NotNull(stream);
            Assert.True(stream.CanRead);
            Assert.False(stream.CanWrite);
            Assert.False(stream.CanSeek);
        }

        [Fact]
        public void GivenStartOffsetProvided_WhenCreatingStream_ThenPositionIsSet()
        {
            // Arrange
            long startOffset = 100;
            Func<Task<BlobClient>> blobClientFactory = () => Task.FromResult(_blobClient);

            // Act
            using var stream = new AzureBlobSourceStream(blobClientFactory, startOffset, _logger);

            // Assert
            Assert.Equal(startOffset, stream.Position);
        }

        [Fact]
        public void GivenNullStartOffset_WhenCreatingStream_ThenPositionIsZero()
        {
            // Arrange
            Func<Task<BlobClient>> blobClientFactory = () => Task.FromResult(_blobClient);

            // Act
            using var stream = new AzureBlobSourceStream(blobClientFactory, null, _logger);

            // Assert
            Assert.Equal(0, stream.Position);
        }

        [Fact]
        public void GivenStream_WhenGettingConcurrentCount_ThenReturnsDefaultValue()
        {
            // Arrange
            Func<Task<BlobClient>> blobClientFactory = () => Task.FromResult(_blobClient);

            // Act
            using var stream = new AzureBlobSourceStream(blobClientFactory, 0, _logger);

            // Assert
            Assert.Equal(3, stream.ConcurrentCount);
        }

        [Fact]
        public void GivenStream_WhenSettingConcurrentCount_ThenValueIsSet()
        {
            // Arrange
            Func<Task<BlobClient>> blobClientFactory = () => Task.FromResult(_blobClient);

            // Act
            using var stream = new AzureBlobSourceStream(blobClientFactory, 0, _logger);
            stream.ConcurrentCount = 5;

            // Assert
            Assert.Equal(5, stream.ConcurrentCount);
        }

        [Fact]
        public void GivenStream_WhenGettingBlockBufferSize_ThenReturnsDefaultValue()
        {
            // Arrange
            Func<Task<BlobClient>> blobClientFactory = () => Task.FromResult(_blobClient);

            // Act
            using var stream = new AzureBlobSourceStream(blobClientFactory, 0, _logger);

            // Assert
            Assert.Equal(8 * 1024 * 1024, stream.BlockBufferSize);
        }

        [Fact]
        public void GivenStream_WhenSettingBlockBufferSize_ThenValueIsSet()
        {
            // Arrange
            Func<Task<BlobClient>> blobClientFactory = () => Task.FromResult(_blobClient);

            // Act
            using var stream = new AzureBlobSourceStream(blobClientFactory, 0, _logger);
            stream.BlockBufferSize = 1024;

            // Assert
            Assert.Equal(1024, stream.BlockBufferSize);
        }

        [Fact]
        public void GivenStream_WhenAccessingLength_ThenThrowsNotImplementedException()
        {
            // Arrange
            Func<Task<BlobClient>> blobClientFactory = () => Task.FromResult(_blobClient);
            using var stream = new AzureBlobSourceStream(blobClientFactory, 0, _logger);

            // Act & Assert
            Assert.Throws<NotImplementedException>(() => stream.Length);
        }

        [Fact]
        public void GivenStream_WhenSettingPosition_ThenThrowsNotImplementedException()
        {
            // Arrange
            Func<Task<BlobClient>> blobClientFactory = () => Task.FromResult(_blobClient);
            using var stream = new AzureBlobSourceStream(blobClientFactory, 0, _logger);

            // Act & Assert
            Assert.Throws<NotImplementedException>(() => stream.Position = 100);
        }

        [Fact]
        public void GivenStream_WhenCallingFlush_ThenThrowsNotImplementedException()
        {
            // Arrange
            Func<Task<BlobClient>> blobClientFactory = () => Task.FromResult(_blobClient);
            using var stream = new AzureBlobSourceStream(blobClientFactory, 0, _logger);

            // Act & Assert
            Assert.Throws<NotImplementedException>(() => stream.Flush());
        }

        [Fact]
        public void GivenStream_WhenCallingSeek_ThenThrowsNotImplementedException()
        {
            // Arrange
            Func<Task<BlobClient>> blobClientFactory = () => Task.FromResult(_blobClient);
            using var stream = new AzureBlobSourceStream(blobClientFactory, 0, _logger);

            // Act & Assert
            Assert.Throws<NotImplementedException>(() => stream.Seek(0, SeekOrigin.Begin));
        }

        [Fact]
        public void GivenStream_WhenCallingSetLength_ThenThrowsNotImplementedException()
        {
            // Arrange
            Func<Task<BlobClient>> blobClientFactory = () => Task.FromResult(_blobClient);
            using var stream = new AzureBlobSourceStream(blobClientFactory, 0, _logger);

            // Act & Assert
            Assert.Throws<NotImplementedException>(() => stream.SetLength(100));
        }

        [Fact]
        public void GivenStream_WhenCallingWrite_ThenThrowsNotImplementedException()
        {
            // Arrange
            Func<Task<BlobClient>> blobClientFactory = () => Task.FromResult(_blobClient);
            using var stream = new AzureBlobSourceStream(blobClientFactory, 0, _logger);
            byte[] buffer = new byte[10];

            // Act & Assert
            Assert.Throws<NotImplementedException>(() => stream.Write(buffer, 0, buffer.Length));
        }

        [Fact]
        public void GivenSmallBlob_WhenReadingData_ThenDataIsRead()
        {
            // Arrange
            byte[] blobData = Encoding.UTF8.GetBytes("Test content");
            SetupBlobClient(blobData);

            Func<Task<BlobClient>> blobClientFactory = () => Task.FromResult(_blobClient);
            using var stream = new AzureBlobSourceStream(blobClientFactory, 0, _logger);
            stream.BlockBufferSize = 100;

            byte[] buffer = new byte[blobData.Length];

            // Act
            int bytesRead = stream.Read(buffer, 0, buffer.Length);

            // Assert
            Assert.Equal(blobData.Length, bytesRead);
            Assert.Equal(blobData, buffer);
        }

        [Fact]
        public void GivenEmptyBlob_WhenReadingData_ThenReturnsZero()
        {
            // Arrange
            byte[] blobData = Array.Empty<byte>();
            SetupBlobClient(blobData);

            Func<Task<BlobClient>> blobClientFactory = () => Task.FromResult(_blobClient);
            using var stream = new AzureBlobSourceStream(blobClientFactory, 0, _logger);

            byte[] buffer = new byte[100];

            // Act
            int bytesRead = stream.Read(buffer, 0, buffer.Length);

            // Assert
            Assert.Equal(0, bytesRead);
        }

        [Fact]
        public void GivenBlobWithStartOffset_WhenReadingData_ThenReadsFromOffset()
        {
            // Arrange
            byte[] blobData = Encoding.UTF8.GetBytes("Test content for offset reading");
            long startOffset = 5;
            SetupBlobClient(blobData);

            Func<Task<BlobClient>> blobClientFactory = () => Task.FromResult(_blobClient);
            using var stream = new AzureBlobSourceStream(blobClientFactory, startOffset, _logger);
            stream.BlockBufferSize = 100;

            byte[] buffer = new byte[10];

            // Act
            int bytesRead = stream.Read(buffer, 0, buffer.Length);

            // Assert
            Assert.Equal(10, bytesRead);
        }

        private void SetupBlobClient(byte[] blobData)
        {
            var blobProperties = BlobsModelFactory.BlobProperties(contentLength: blobData.Length);
            var propertiesResponse = Response.FromValue(blobProperties, Substitute.For<Response>());
            _blobClient.GetProperties(default, default).ReturnsForAnyArgs(propertiesResponse);

            var contentStream = new MemoryStream(blobData);
            var blobDownloadStreamingResult = BlobsModelFactory.BlobDownloadStreamingResult(content: contentStream);
            var downloadResponse = Response.FromValue(blobDownloadStreamingResult, Substitute.For<Response>());

            _blobClient.DownloadStreamingAsync(Arg.Any<BlobDownloadOptions>(), default)
                .Returns(callInfo =>
                {
                    var options = callInfo.ArgAt<BlobDownloadOptions>(0);
                    long offset = options?.Range.Offset ?? 0;
                    long? length = options?.Range.Length;

                    var dataToReturn = new byte[length ?? (blobData.Length - offset)];
                    Array.Copy(blobData, offset, dataToReturn, 0, dataToReturn.Length);

                    var resultStream = new MemoryStream(dataToReturn);
                    var result = BlobsModelFactory.BlobDownloadStreamingResult(content: resultStream);
                    return Response.FromValue(result, Substitute.For<Response>());
                });
        }
    }
}
