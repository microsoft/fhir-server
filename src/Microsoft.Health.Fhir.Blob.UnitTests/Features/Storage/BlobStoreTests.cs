﻿// -------------------------------------------------------------------------------------------------
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
using DotLiquid.Util;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Logging;
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
    private static readonly string _resourceFilePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "pao-resources.ndjson");

    private static readonly RecyclableMemoryStreamManager RecyclableMemoryStreamManagerInstance = new RecyclableMemoryStreamManager();
    protected const long DefaultStorageIdentifier = 101010101010;
    protected const long SecondaryStorageIdentifier = 202020202020;

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

    internal static ResourceWrapper CreateTestResourceWrapperForWriting(string rawResourceContent)
    {
        // We are only interested in the raw resource content from this object, setting dummy values for other ctor arguements
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

    internal static ResourceWrapper CreateTestResourceWrapperForReading(int offset)
    {
        // We are only interested in the storage identifier and offset. Raw resource content will be null
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

        resourceWrapper.ResourceStorageIdentifier = DefaultStorageIdentifier;
        resourceWrapper.ResourceStorageOffset = offset;
        return resourceWrapper;
    }

    internal static IReadOnlyList<ResourceWrapper> GetResourceWrappersWithData()
    {
        var resourceWrappers = new List<ResourceWrapper>();
        using var stream = new FileStream(_resourceFilePath, FileMode.Open, FileAccess.Read);
        using var reader = new StreamReader(stream);
        string line;
        while ((line = reader.ReadLine()) != null)
        {
            var resourceWrapper = CreateTestResourceWrapperForWriting(line);
            resourceWrappers.Add(resourceWrapper);
        }

        return resourceWrappers;
    }

    internal static IReadOnlyList<ResourceWrapper> GetResourceWrappersMetaData(IReadOnlyList<ResourceWrapper> resources)
    {
        var resourceWrappersWithMetadata = new List<ResourceWrapper>();

        var expectedOffsets = new List<int>();
        int lastOffset = 0;

        for (int i = 0; i < resources.Count; i++)
        {
            if (i == 0)
            {
                // Add first element
                expectedOffsets.Add(0);
                var tempResourceWrapper = CreateTestResourceWrapperForReading(0);
                resourceWrappersWithMetadata.Add(tempResourceWrapper);
            }

            // No need to process length of last resource, since offset for next resource doesn't exist
            if (i < resources.Count - 1)
            {
                var currentOffset = lastOffset + resources[0].RawResource.Data.Length + BlobRawResourceStore.EndOfLine + 1;
                expectedOffsets.Add(currentOffset);
                lastOffset = currentOffset;

                var tempResourceWrapper = CreateTestResourceWrapperForReading(currentOffset);
                resourceWrappersWithMetadata.Add(tempResourceWrapper);
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
    public async Task GivenResourceStore_WhenSingleDownloadSucceeds_ThenValidateFirstRawResourceInBlob()
    {
        InitializeBlobStore(out BlobRawResourceStore blobFileStore, out TestBlobClient client);

        var resourcesWithData = GetResourceWrappersWithData();
        var resourceWrappersMetaData = GetResourceWrappersMetaData(resourcesWithData);

        // Validate first element
        var firstElementOffset = resourceWrappersMetaData[0].ResourceStorageOffset;

        var memoryStream = GetBlobDownloadStreamingPartialResult(firstElementOffset);
        var streamingResult = BlobsModelFactory.BlobDownloadStreamingResult(
            content: memoryStream,
            details: BlobsModelFactory.BlobDownloadDetails());

        // Instead of substituting for Response<T>, create a real instance:
        Response<BlobDownloadStreamingResult> response =
            Response.FromValue(streamingResult, new FakeResponse() { ContentStream = streamingResult.Content });

        // Configure substitute client to return desired response.
        client.BlockBlobClient.DownloadStreamingAsync(
            Arg.Any<BlobDownloadOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));

        var firstResource = await blobFileStore.ReadRawResourceAsync(DefaultStorageIdentifier, firstElementOffset, CancellationToken.None);
        Assert.NotNull(firstResource);
        Assert.Equal(resourcesWithData[0].RawResource.Data, firstResource.RawResource.Data);
    }

    [Fact]
    public async Task GivenResourceStore_WhenSingleDownloadSucceeds_ThenValidateMiddleRawResourceInBlob()
    {
        InitializeBlobStore(out BlobRawResourceStore blobFileStore, out TestBlobClient client);

        var resourcesWithData = GetResourceWrappersWithData();
        var resourceWrappersMetaData = GetResourceWrappersMetaData(resourcesWithData);

        // Initialize to first element, in case there are less than 2 elements
        var middleElementOffset = resourceWrappersMetaData[0].ResourceStorageOffset;

        int middleIndex = resourceWrappersMetaData.Count / 2;
        if (resourceWrappersMetaData.Count > 2)
        {
            middleElementOffset = resourceWrappersMetaData[middleIndex].ResourceStorageOffset;
        }

        var memoryStream = GetBlobDownloadStreamingPartialResult(middleElementOffset);
        var streamingResult = BlobsModelFactory.BlobDownloadStreamingResult(
            content: memoryStream,
            details: BlobsModelFactory.BlobDownloadDetails());

        // Instead of substituting for Response<T>, create a real instance:
        Response<BlobDownloadStreamingResult> response =
            Response.FromValue(streamingResult, new FakeResponse() { ContentStream = streamingResult.Content });

        // Configure substitute client to return desired response.
        client.BlockBlobClient.DownloadStreamingAsync(
            Arg.Any<BlobDownloadOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));

        var middleResource = await blobFileStore.ReadRawResourceAsync(DefaultStorageIdentifier, middleElementOffset, CancellationToken.None);
        Assert.NotNull(middleResource);
        Assert.Equal(resourcesWithData[middleIndex].RawResource.Data, middleResource.RawResource.Data);
    }

    [Fact]
    public async Task GivenResourceStore_WhenSingleDownloadSucceeds_ThenValidateLastRawResourceInBlob()
    {
        InitializeBlobStore(out BlobRawResourceStore blobFileStore, out TestBlobClient client);

        var resourcesWithData = GetResourceWrappersWithData();
        var resourceWrappersMetaData = GetResourceWrappersMetaData(resourcesWithData);

        // Validate first, middle and last elements
        var lastElementOffset = resourceWrappersMetaData[resourceWrappersMetaData.Count - 1].ResourceStorageOffset;

        var memoryStream = GetBlobDownloadStreamingPartialResult(lastElementOffset);
        var streamingResult = BlobsModelFactory.BlobDownloadStreamingResult(
            content: memoryStream,
            details: BlobsModelFactory.BlobDownloadDetails());

        // Instead of substituting for Response<T>, create a real instance:
        Response<BlobDownloadStreamingResult> response =
            Response.FromValue(streamingResult, new FakeResponse() { ContentStream = streamingResult.Content });

        // Configure substitute client to return desired response.
        client.BlockBlobClient.DownloadStreamingAsync(
            Arg.Any<BlobDownloadOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));

        var lastResource = await blobFileStore.ReadRawResourceAsync(DefaultStorageIdentifier, lastElementOffset, CancellationToken.None);
        Assert.NotNull(lastResource);
        Assert.Equal(resourcesWithData[resourceWrappersMetaData.Count - 1].RawResource.Data, lastResource.RawResource.Data);
    }

    [Fact]
    public async Task GivenResourceStore_WhenMultipleDownloadSucceeds_ThenValidateRawResources()
    {
        InitializeBlobStore(out BlobRawResourceStore blobFileStore, out TestBlobClient client);

        var resourcesWithData = GetResourceWrappersWithData();
        var resourceWrappersMetaData = GetResourceWrappersMetaData(resourcesWithData);

        var memoryStream = GetBlobDownloadStreamingPartialResult(0);
        var streamingResult = BlobsModelFactory.BlobDownloadStreamingResult(
            content: memoryStream,
            details: BlobsModelFactory.BlobDownloadDetails());

        // Instead of substituting for Response<T>, create a real instance:
        Response<BlobDownloadStreamingResult> response =
            Response.FromValue(streamingResult, new FakeResponse() { ContentStream = streamingResult.Content });

        // Configure substitute client to return desired response.
        client.BlockBlobClient.DownloadStreamingAsync(
            Arg.Any<BlobDownloadOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));

        var rawResourceResults = await blobFileStore.ReadRawResourcesAsync(resourceWrappersMetaData, CancellationToken.None);
        Assert.NotNull(rawResourceResults);
        Assert.Equal(resourceWrappersMetaData.Count, rawResourceResults.Count);

        /*foreach (ResourceWrapper wrapper in resourceWrappersMetaData)
        {
            // Match by offset
            var rawData = rawResourceResults.First<ResourceWrapper>(r => r.ResourceStorageOffset == wrapper.ResourceStorageOffset);
            Assert.Equal(wrapper.RawResource, rawData);
        }*/
    }

    [Fact]
    public void TestGetBlobOffsetCombinations()
    {
        var rawResources = new List<ResourceWrapper>();

        var tempRawResource = CreateTestResourceWrapperForReading(0);
        tempRawResource.ResourceStorageIdentifier = DefaultStorageIdentifier;
        rawResources.Add(tempRawResource);

        tempRawResource = CreateTestResourceWrapperForReading(9000);
        tempRawResource.ResourceStorageIdentifier = SecondaryStorageIdentifier;
        rawResources.Add(tempRawResource);

        tempRawResource = CreateTestResourceWrapperForReading(6000);
        tempRawResource.ResourceStorageIdentifier = DefaultStorageIdentifier;
        rawResources.Add(tempRawResource);

        tempRawResource = CreateTestResourceWrapperForReading(3700);
        tempRawResource.ResourceStorageIdentifier = SecondaryStorageIdentifier;
        rawResources.Add(tempRawResource);

        tempRawResource = CreateTestResourceWrapperForReading(2500);
        tempRawResource.ResourceStorageIdentifier = DefaultStorageIdentifier;
        rawResources.Add(tempRawResource);

        var resultDictionary = BlobRawResourceStore.GetBlobOffsetCombinations(rawResources);
        Assert.NotNull(resultDictionary);
        Assert.Equal(2, resultDictionary.Count);
        Assert.Equal(3, resultDictionary[DefaultStorageIdentifier].Count);
        Assert.Equal(2, resultDictionary[SecondaryStorageIdentifier].Count);
    }

    [Fact]
    public async Task TestFindResourcesInStreamAsync()
    {
        var resourcesWithData = GetResourceWrappersWithData();
        var resourceWrappersMetaData = GetResourceWrappersMetaData(resourcesWithData);

        var offsetList = resourceWrappersMetaData.Select(x => x.ResourceStorageOffset).ToList();
        var results = await BlobRawResourceStore.FindResourcesInStreamAsync(GetBlobDownloadStreamingPartialResult(0), DefaultStorageIdentifier, offsetList);
        Assert.NotNull(results);

        foreach (int offset in offsetList)
        {
            var expectedData = resourcesWithData.First(x => x.ResourceStorageOffset == offset).RawResource.Data;
            var actualData = results.First(x => x.ResourceStorageOffset == offset).RawResource.Data;
            Assert.Equal(expectedData, actualData);
        }
    }

    private MemoryStream GetBlobDownloadStreamingPartialResult(int offset = 0)
    {
        // Read file stream.txt
        using var stream = new FileStream(_resourceFilePath, FileMode.Open, FileAccess.Read);

        stream.Seek(offset, SeekOrigin.Begin);

        // Create a MemoryStream and copy the file stream to it
        var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        memoryStream.Position = 0;
        return memoryStream;
    }
}
