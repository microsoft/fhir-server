// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Health.Fhir.Blob.Features.Storage;
using NSubstitute;

namespace Microsoft.Health.Fhir.Blob.UnitTests.Fixtures;

internal sealed class TestBlobClient : IBlobClient
{
    public TestBlobClient()
    {
        BlobContainerClient = Substitute.For<BlobContainerClient>();
        BlockBlobClient = Substitute.For<BlockBlobClient>();
        BlobContainerClient.GetBlockBlobClient(Arg.Any<string>()).Returns(BlockBlobClient);
    }

    public BlobContainerClient BlobContainerClient { get; private set; }

    public BlockBlobClient BlockBlobClient { get; private set; }
}
