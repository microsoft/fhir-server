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
    protected const long SecondaryStorageIdentifier = 202020202020;
    internal static readonly int EndOfLine = Encoding.UTF8.GetByteCount(Environment.NewLine);
    internal static readonly byte FirstOffset = 3;

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
        return resourceWrapper;
    }

    internal static ResourceWrapper CreateTestResourceWrapper(long storageIdentifier, int offset)
    {
        var resourceWrapper = new ResourceWrapper(
            "resourceId",
            "versionId",
            "resourceTypeName",
            null,
            null,
            DateTimeOffset.UtcNow,
            false,
            null,
            null,
            null);

        resourceWrapper.ResourceStorageIdentifier = storageIdentifier;
        resourceWrapper.ResourceStorageOffset = offset;
        return resourceWrapper;
    }

    internal IReadOnlyList<ResourceWrapper> GetResourceWrappersWithData()
    {
        var resourceWrappers = new List<ResourceWrapper>();
        using var stream = GetBlobDownloadStreamingPartialResult(0);
        using var reader = new StreamReader(stream, new UTF8Encoding(false));
        string line;
        int characterPosition = FirstOffset;

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
        int lastOffset = FirstOffset;

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

        var ex = await Assert.ThrowsAsync<RawResourceStoreException>(() => blobFileStore.WriteRawResourcesAsync(Substitute.For<IReadOnlyList<ResourceWrapper>>(), DefaultStorageIdentifier, CancellationToken.None));

        Assert.Equal(Resources.RawResourceStoreOperationFailed, ex.Message);
    }

    [Fact]
    public async Task GivenResourceStore_WhenUploadSucceeds_ThenValidateStorageDetails()
    {
        InitializeBlobStore(out BlobRawResourceStore blobFileStore, out TestBlobClient client);
        client.BlockBlobClient.UploadAsync(Arg.Any<Stream>(), Arg.Any<BlobUploadOptions>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(Substitute.For<Response<BlobContentInfo>>()));

        var result = await blobFileStore.WriteRawResourcesAsync(GetResourceWrappersWithData(), DefaultStorageIdentifier, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(DefaultStorageIdentifier, result[0].ResourceStorageIdentifier);
        Assert.Equal(DefaultStorageIdentifier, result[1].ResourceStorageIdentifier);
        Assert.Equal(0, result[0].ResourceStorageOffset);

        var expectedOffset = result[0].RawResource.Data.Length + BlobRawResourceStore.EndOfLine;

        // This offset is based on the test data in the resources.ndjson file.
        Assert.Equal(expectedOffset, result[1].ResourceStorageOffset);
    }

    [Fact]
    public async Task GivenResourceStore_WhenUploadFails_ThenThrowExceptionWithRightMessageAndErrorCode()
    {
        InitializeBlobStore(out BlobRawResourceStore blobFileStore, out TestBlobClient client);

        RequestFailedException requestFailedAuthException = new RequestFailedException(
            status: 400,
            message: "auth failed simulation",
            errorCode: BlobErrorCode.AuthenticationFailed.ToString(),
            innerException: new Exception("super secret inner info"));

        client.BlockBlobClient.UploadAsync(Arg.Any<Stream>(), Arg.Any<BlobUploadOptions>(), Arg.Any<CancellationToken>()).Throws(requestFailedAuthException);

        var ex = await Assert.ThrowsAsync<RawResourceStoreException>(() => blobFileStore.WriteRawResourcesAsync(Substitute.For<IReadOnlyList<ResourceWrapper>>(), DefaultStorageIdentifier, CancellationToken.None));
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

    [Fact]
    public void TestGetBlobOffsetCombinations()
    {
        var rawResources = new List<RawResourceLocator>();

        var tempRawResource = new RawResourceLocator(DefaultStorageIdentifier, 0);
        rawResources.Add(tempRawResource);

        tempRawResource = new RawResourceLocator(SecondaryStorageIdentifier, 9000);
        rawResources.Add(tempRawResource);

        tempRawResource = new RawResourceLocator(DefaultStorageIdentifier, 6000);
        rawResources.Add(tempRawResource);

        tempRawResource = new RawResourceLocator(SecondaryStorageIdentifier, 3700);
        rawResources.Add(tempRawResource);

        tempRawResource = new RawResourceLocator(DefaultStorageIdentifier, 2500);
        rawResources.Add(tempRawResource);

        var resultDictionary = BlobRawResourceStore.GetBlobOffsetCombinations(rawResources);
        Assert.NotNull(resultDictionary);
        Assert.Equal(2, resultDictionary.Count);
        Assert.Equal(3, resultDictionary[DefaultStorageIdentifier].Count);
        Assert.Equal(2, resultDictionary[SecondaryStorageIdentifier].Count);
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

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GivenResourceStore_WhenReadingSingleResource_ThenValidateContent(int resourceNumber)
    {
        InitializeBlobStore(out BlobRawResourceStore blobFileStore, out TestBlobClient client);
        var resourceWrappersMetaData = GetResourceWrappersMetaData(GetResourceWrappersWithData());

        var key = new RawResourceLocator(resourceWrappersMetaData[resourceNumber].ResourceStorageIdentifier, resourceWrappersMetaData[resourceNumber].ResourceStorageOffset);
        var expectedContent = resourceWrappersMetaData[resourceNumber].RawResource.Data;

        var memoryStream = GetBlobDownloadStreamingPartialResult(0);
        client.BlockBlobClient.OpenReadAsync(Arg.Any<BlobOpenReadOptions>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult((Stream)memoryStream));

        var result = await blobFileStore.ReadRawResourcesAsync(new List<RawResourceLocator> { key }, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(expectedContent, result[key]);
    }

    [Fact]
    public async Task GivenResourceStore_WhenReadingMultipleResources_ThenValidateContents()
    {
        InitializeBlobStore(out BlobRawResourceStore blobFileStore, out TestBlobClient client);
        var resourceWrappersMetaData = GetResourceWrappersMetaData(GetResourceWrappersWithData());

        var resourceLocators = resourceWrappersMetaData.Select(wrapper => new RawResourceLocator(wrapper.ResourceStorageIdentifier, wrapper.ResourceStorageOffset)).ToList();
        var memoryStream = GetBlobDownloadStreamingPartialResult(0);
        client.BlockBlobClient.OpenReadAsync(Arg.Any<BlobOpenReadOptions>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult((Stream)memoryStream));

        var result = await blobFileStore.ReadRawResourcesAsync(resourceLocators, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(resourceWrappersMetaData.Count, result.Count);

        foreach (var wrapper in resourceWrappersMetaData)
        {
            var key = new RawResourceLocator(wrapper.ResourceStorageIdentifier, wrapper.ResourceStorageOffset);
            Assert.True(result.ContainsKey(key));
            Assert.Equal(wrapper.RawResource.Data, result[key]);
        }
    }
}
