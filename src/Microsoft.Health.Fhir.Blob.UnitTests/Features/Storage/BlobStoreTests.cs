// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Blob.Configs;
using Microsoft.Health.Fhir.Blob.Features.Common;
using Microsoft.Health.Fhir.Blob.Features.Storage;
using Microsoft.Health.Fhir.Blob.UnitTests.Fixtures;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.IO;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Microsoft.Health.Fhir.Blob.UnitTests.Features.Storage;

public class BlobStoreTests
{
    private static readonly string _resourceFilePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "resources.ndjson");

    private static readonly RecyclableMemoryStreamManager RecyclableMemoryStreamManagerInstance = new RecyclableMemoryStreamManager();
    protected const long DefaultStorageIdentifier = 101010101010;
    internal static readonly int EndOfLine = Encoding.UTF8.GetByteCount(Environment.NewLine);

    internal static void InitializeBlobStore(out BlobRawResourceStore blobStore, out TestBlobClient blobClient)
    {
        var clientOptions = Substitute.For<IOptions<BlobServiceClientOptions>>();
        clientOptions.Value.Returns(Substitute.For<BlobServiceClientOptions>());

        blobClient = new TestBlobClient();
        var blobContainerClient = Substitute.For<BlobContainerClient>();
        blobContainerClient.GetBlockBlobClient(Arg.Any<string>()).Returns(Substitute.For<BlockBlobClient>());

        var options = Substitute.For<IOptions<BlobOperationOptions>>();
        options.Value.Returns(Substitute.For<BlobOperationOptions>());

        var storeLogger = NullLogger<BlobRawResourceStore>.Instance;

        blobStore = new BlobRawResourceStore(blobClient, storeLogger, options, RecyclableMemoryStreamManagerInstance);
    }

    internal static ResourceWrapper CreateTestResourceWrapper(string rawResourceContent)
    {
        // We are only interested in the raw resource content from this object, setting dummy values for other ctor arguments
        var rawResource = Substitute.For<RawResource>(rawResourceContent, FhirResourceFormat.Json, false);
        var resourceWrapper = new ResourceWrapper(
            "resourceId",
            "versionId",
            "resourceTypeName",
            rawResource,
            null,
            DateTimeOffset.UtcNow,
            false,
            null,
            null,
            null);

        return resourceWrapper;
    }

    internal static ResourceWrapper CreateTestResourceWrapper(long storageIdentifier, int offset, string rawResourceContent)
    {
        var rawResource = Substitute.For<RawResource>(rawResourceContent, FhirResourceFormat.Json, false);
        var resourceWrapper = new ResourceWrapper(
            "resourceId",
            "versionId",
            "resourceTypeName",
            rawResource,
            null,
            DateTimeOffset.UtcNow,
            false,
            null,
            null,
            null);

        resourceWrapper.ResourceStorageIdentifier = storageIdentifier;
        resourceWrapper.ResourceStorageOffset = offset;
        resourceWrapper.ResourceLength = Encoding.UTF8.GetByteCount(rawResourceContent);
        return resourceWrapper;
    }

    internal IReadOnlyList<ResourceWrapper> GetResourceWrappersWithData()
    {
        var resourceWrappers = new List<ResourceWrapper>();
        using var stream = GetBlobDownloadStreamingPartialResult(0);
        using var reader = new StreamReader(stream, new UTF8Encoding(false));
        string line;
        int characterPosition = 0;

        while ((line = reader.ReadLine()) != null)
        {
            characterPosition += Encoding.UTF8.GetByteCount(line) + EndOfLine;
            var resourceWrapper = CreateTestResourceWrapper(line);
            resourceWrappers.Add(resourceWrapper);
        }

        return resourceWrappers;
    }

    internal static IReadOnlyList<ResourceWrapper> GetResourceWrappersMetaData(IReadOnlyList<ResourceWrapper> resources)
    {
        var resourceWrappersWithMetadata = new List<ResourceWrapper>();
        int lastOffset = 0;

        for (int i = 0; i < resources.Count; i++)
        {
            var tempResourceWrapper = CreateTestResourceWrapper(DefaultStorageIdentifier, lastOffset, resources[i].RawResource.Data);
            resourceWrappersWithMetadata.Add(tempResourceWrapper);

            // No need to process length of last resource, since offset for next resource doesn't exist
            if (i < resources.Count - 1)
            {
                lastOffset = lastOffset + resources[i].RawResource.Data.Length + BlobRawResourceStore.EndOfLine;
            }
        }

        return resourceWrappersWithMetadata;
    }

    [Fact]
    public async Task GivenResourceStore_WhenUploadFails_ThenThrowExceptionWithRightMessage()
    {
        InitializeBlobStore(out BlobRawResourceStore blobFileStore, out TestBlobClient client);
        client.BlockBlobClient.UploadAsync(Arg.Any<Stream>(), Arg.Any<BlobUploadOptions>(), Arg.Any<CancellationToken>()).Throws(new System.Exception());

        var resources = GetResourceWrappersWithData();
        var ex = await Assert.ThrowsAsync<RawResourceStoreException>(() => blobFileStore.WriteRawResourcesAsync(resources, DefaultStorageIdentifier, CancellationToken.None));

        Assert.Equal(Resources.RawResourceStoreOperationFailed, ex.Message);
    }

    [Fact]
    public async Task GivenResourceStore_WhenUploadSucceeds_ThenValidateStorageDetails()
    {
        InitializeBlobStore(out BlobRawResourceStore blobFileStore, out TestBlobClient client);
        client.BlockBlobClient.UploadAsync(Arg.Any<Stream>(), Arg.Any<BlobUploadOptions>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(Substitute.For<Response<BlobContentInfo>>()));

        var resources = GetResourceWrappersWithData();
        var result = await blobFileStore.WriteRawResourcesAsync(resources, DefaultStorageIdentifier, CancellationToken.None);

        int offset = 0;
        Assert.NotNull(result);
        for (int i = 0; i < result.Count; i++)
        {
            Assert.Equal(DefaultStorageIdentifier, result[i].ResourceStorageIdentifier);
            Assert.Equal(offset, result[i].ResourceStorageOffset);
            Assert.Equal(Encoding.UTF8.GetByteCount(resources[i].RawResource.Data), result[i].ResourceLength);
            offset += Encoding.UTF8.GetByteCount(resources[i].RawResource.Data) + EndOfLine;
        }
    }

    [Fact]
    public async Task GivenResourceStore_WhenUploadFails_ThenThrowExceptionWithRightMessageAndErrorCode()
    {
        InitializeBlobStore(out BlobRawResourceStore blobFileStore, out TestBlobClient client);

        var resources = GetResourceWrappersWithData();
        RequestFailedException requestFailedAuthException = new RequestFailedException(
            status: 400,
            message: "auth failed simulation",
            errorCode: BlobErrorCode.AuthenticationFailed.ToString(),
            innerException: new Exception("super secret inner info"));

        client.BlockBlobClient.UploadAsync(Arg.Any<Stream>(), Arg.Any<BlobUploadOptions>(), Arg.Any<CancellationToken>()).Throws(requestFailedAuthException);

        var ex = await Assert.ThrowsAsync<RawResourceStoreException>(() => blobFileStore.WriteRawResourcesAsync(resources, DefaultStorageIdentifier, CancellationToken.None));
        Assert.Equal(string.Format(CultureInfo.InvariantCulture, Resources.RawResourceStoreOperationFailedWithError, BlobErrorCode.AuthenticationFailed), ex.Message);
    }

    [Theory]
    [InlineData(1234567890)]
    [InlineData(-1746527485)]
    [InlineData(8746373845756548765)]
    [InlineData(-8746373845756548765)]
    [InlineData(547)]
    [InlineData(-547)]
    public void GivenStorageIdentifier_HashReturnedIsBetween0and998(long input)
    {
        string hash = BlobUtility.ComputeHashPrefixForBlobName(input);
        Assert.InRange(int.Parse(hash), 0, 998);
    }

    [Fact]
    public void GivenStorageIdentifierAsZero_ThrowsArguementException()
    {
        Assert.Throws<ArgumentException>(() => BlobUtility.ComputeHashPrefixForBlobName(0));
    }

    private MemoryStream GetBlobDownloadStreamingPartialResult(int offset = 0)
    {
        var memoryStream = new MemoryStream();
        using (var stream = new FileStream(_resourceFilePath, FileMode.Open, FileAccess.Read))
        {
            stream.Seek(offset, SeekOrigin.Begin);
            stream.CopyTo(memoryStream);
        }

        memoryStream.Position = 0; // Reset the position for further use
        return memoryStream; // Return the MemoryStream for manipulation by other methods
    }

    [Fact]
    public async Task GivenResourceStore_WhenReadingSingleResource_ThenValidateContent()
    {
        // Arrange
        InitializeBlobStore(out BlobRawResourceStore blobFileStore, out TestBlobClient client);
        var resourceWrappersMetaData = GetResourceWrappersMetaData(GetResourceWrappersWithData());

        var key = new RawResourceLocator(
            resourceWrappersMetaData[0].ResourceStorageIdentifier,
            resourceWrappersMetaData[0].ResourceStorageOffset,
            resourceWrappersMetaData[0].ResourceLength);

        var blobDownloadResult = BlobsModelFactory.BlobDownloadResult(content: BinaryData.FromString(resourceWrappersMetaData[0].RawResource.Data));

        var response = Substitute.For<Response<BlobDownloadResult>>();
        response.Value.Returns(blobDownloadResult);

        client.BlockBlobClient
            .DownloadContentAsync(Arg.Any<BlobDownloadOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));

        // Act
        var result = await blobFileStore.ReadRawResourcesAsync(new List<RawResourceLocator> { key }, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(resourceWrappersMetaData[0].RawResource.Data, result.First().Value);
    }

    [Fact]
    public async Task GivenResourceStore_WhenReadingMultipleResources_ThenValidateContents()
    {
        InitializeBlobStore(out BlobRawResourceStore blobFileStore, out TestBlobClient client);
        var resourceWrappersMetaData = GetResourceWrappersMetaData(GetResourceWrappersWithData());

        var resourceLocators = resourceWrappersMetaData.Select(wrapper => new RawResourceLocator(wrapper.ResourceStorageIdentifier, wrapper.ResourceStorageOffset, wrapper.ResourceLength)).ToList();
        var responses = resourceWrappersMetaData.Select(wrapper =>
        {
            var blobDownloadResult = BlobsModelFactory.BlobDownloadResult(content: BinaryData.FromString(wrapper.RawResource.Data));
            var response = Substitute.For<Response<BlobDownloadResult>>();
            response.Value.Returns(blobDownloadResult);
            return response;
        }).ToList();

        client.BlockBlobClient
            .DownloadContentAsync(Arg.Any<BlobDownloadOptions>(), Arg.Any<CancellationToken>())
            .Returns(responses[0], responses[1], responses[2]);

        var result = await blobFileStore.ReadRawResourcesAsync(resourceLocators, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(resourceWrappersMetaData.Count, result.Count);

        for (int i = 0; i < resourceWrappersMetaData.Count; i++)
        {
            Assert.Equal(resourceWrappersMetaData[i].RawResource.Data, result[resourceLocators[i]]);
            Assert.Equal(resourceWrappersMetaData[i].ResourceStorageIdentifier, resourceLocators[i].RawResourceStorageIdentifier);
            Assert.Equal(resourceWrappersMetaData[i].ResourceStorageOffset, resourceLocators[i].RawResourceOffset);
        }
    }

    [Fact]
    public async Task GivenResourceStore_WhenWritingEmptyResourceList_ThenThrowArgumentException()
    {
        // Arrange
        InitializeBlobStore(out BlobRawResourceStore blobFileStore, out TestBlobClient client);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => blobFileStore.WriteRawResourcesAsync(new List<ResourceWrapper>(), DefaultStorageIdentifier, CancellationToken.None));
    }

    [Fact]
    public async Task GivenResourceStore_WhenWritingValidResources_ThenValidateStorageDetails()
    {
        // Arrange
        InitializeBlobStore(out BlobRawResourceStore blobFileStore, out TestBlobClient client);
        client.BlockBlobClient.UploadAsync(Arg.Any<Stream>(), Arg.Any<BlobUploadOptions>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(Substitute.For<Response<BlobContentInfo>>()));

        var resourceWrappers = GetResourceWrappersWithData();

        // Act
        var result = await blobFileStore.WriteRawResourcesAsync(resourceWrappers, DefaultStorageIdentifier, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(resourceWrappers.Count, result.Count);
        Assert.All(result, r => Assert.Equal(DefaultStorageIdentifier, r.ResourceStorageIdentifier));
    }

    [Fact]
    public async Task GivenResourceStore_WhenReadingNonExistentResource_ThenThrowResourceNotFoundException()
    {
        // Arrange
        InitializeBlobStore(out BlobRawResourceStore blobFileStore, out TestBlobClient client);

        var nonExistentKey = new RawResourceLocator(-1, -1, -1); // Invalid key

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => blobFileStore.ReadRawResourcesAsync(new List<RawResourceLocator> { nonExistentKey }, CancellationToken.None));
    }
}
