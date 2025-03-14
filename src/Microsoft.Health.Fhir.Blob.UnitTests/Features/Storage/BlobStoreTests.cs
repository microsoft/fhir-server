// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Core;
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
    private static readonly string _streamFilePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "stream.txt");
    private static readonly RecyclableMemoryStreamManager RecyclableMemoryStreamManagerInstance = new RecyclableMemoryStreamManager();
    protected const long DefaultStorageIdentifier = 101010101010;

    internal static void InitializeBlobStore(out BlobRawResourceStore blobStore, out TestBlobClient blobClient)
    {
        var clientOptions = Substitute.For<IOptions<BlobServiceClientOptions>>();
        clientOptions.Value.Returns(Substitute.For<BlobServiceClientOptions>());

        var blobServiceClient = Substitute.For<BlobServiceClient>();
        var blobContainerConfiguration = Substitute.For<IOptionsMonitor<BlobContainerConfiguration>>();
        var logger = NullLogger<BlobStoreClient>.Instance;

        blobClient = new TestBlobClient();

        var blobContainerClient = Substitute.For<BlobContainerClient>();
        blobContainerClient.GetBlockBlobClient(Arg.Any<string>()).Returns(Substitute.For<BlockBlobClient>());

        var options = Substitute.For<IOptions<BlobOperationOptions>>();
        options.Value.Returns(Substitute.For<BlobOperationOptions>());

        var blobStorerConfiguration = Substitute.For<IOptionsMonitor<BlobContainerConfiguration>>();
        var storeLogger = NullLogger<BlobRawResourceStore>.Instance;

        blobStore = new BlobRawResourceStore(blobClient, storeLogger, options, RecyclableMemoryStreamManagerInstance);
    }

    internal static ResourceWrapper CreateTestResourceWrapper(string rawResourceContent)
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

    internal static IReadOnlyList<ResourceWrapper> GetResourceWrappersWithData()
    {
        var resourceWrappers = new List<ResourceWrapper>();
        using var stream = new FileStream(_resourceFilePath, FileMode.Open, FileAccess.Read);
        using var reader = new StreamReader(stream);
        string line;
        while ((line = reader.ReadLine()) != null)
        {
            var resourceWrapper = CreateTestResourceWrapper(line);
            resourceWrappers.Add(resourceWrapper);
        }

        return resourceWrappers;
    }

    internal static Stream GetBlobDownloadStreamingResult()
    {
        return new FileStream(_streamFilePath, FileMode.Open, FileAccess.Read);

        // return new MemoryStream(Encoding.UTF8.GetBytes("{\"resourceType\":\"Patient\"}"));
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

        // This offset is based on the test data in the resources.ndjson file.
        Assert.Equal(2997, result[1].ResourceStorageOffset);
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
        Assert.Equal(string.Format(CultureInfo.InvariantCulture, Resources.RawResourceStoreOperationFailedWithError, BlobErrorCode.AuthenticationFailed.ToString()), ex.Message);
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
    public async Task GivenResourceStore_WhenDownloadSucceeds_ThenValidateRawResource()
    {
        long testResourceStorageIdentifier = 1010101010;
        int offset = 2997;
        var resources = GetResourceWrappersWithData();

        InitializeBlobStore(out BlobRawResourceStore blobFileStore, out TestBlobClient client);
        var substituteBlobContainerClient = Substitute.For<BlockBlobClient>();
        substituteBlobContainerClient.UploadAsync(Arg.Any<Stream>(), Arg.Any<BlobUploadOptions>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(Substitute.For<Response<BlobContentInfo>>()));

        var memoryStream = GetBlobDownloadStreamingResult(); // Your method to get a valid Stream.
        var streamingResult = BlobsModelFactory.BlobDownloadStreamingResult(
            content: memoryStream,
            details: BlobsModelFactory.BlobDownloadDetails());

        // Instead of substituting for Response<T>, create a real instance:
        Response<BlobDownloadStreamingResult> response =
            Response.FromValue(streamingResult, new FakeResponse() { ContentStream = streamingResult.Content});

        // Configure substitute client to return desired response.
        substituteBlobContainerClient.DownloadStreamingAsync(
            Arg.Any<BlobDownloadOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));

        // Read the data back
        var rawResource1 = await blobFileStore.ReadRawResourceAsync(testResourceStorageIdentifier, 0, CancellationToken.None);
        var rawResource2 = await blobFileStore.ReadRawResourceAsync(testResourceStorageIdentifier, offset, CancellationToken.None);

        Assert.NotNull(rawResource1);
        Assert.NotNull(rawResource2);
        Assert.Equal(resources[0].RawResource.Data, rawResource1.Data);
        Assert.Equal(resources[1].RawResource.Data, rawResource2.Data);
    }
}
