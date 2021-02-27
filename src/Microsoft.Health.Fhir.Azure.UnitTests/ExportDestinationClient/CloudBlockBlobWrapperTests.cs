// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Health.Fhir.Azure.ExportDestinationClient;
using Xunit;

namespace Microsoft.Health.Fhir.Azure.UnitTests.ExportDestinationClient
{
    public class CloudBlockBlobWrapperTests
    {
        private const string StorageAddress = "https://basestorage/";
        private const string ContainerId = "containerId";
        private const string BlobIdWithPrefix = "/blob1";

        private Uri _blobUri;
        private BlockBlobClient _cloudBlockBlob;
        private CloudBlockBlobWrapper _blobWrapper;

        public CloudBlockBlobWrapperTests()
        {
            _blobUri = new Uri(StorageAddress + ContainerId + BlobIdWithPrefix);
            _cloudBlockBlob = new BlockBlobClient(_blobUri);
            _blobWrapper = new CloudBlockBlobWrapper(_cloudBlockBlob);
        }

        [Fact]
        public void GivenNewCloudBlockBlobNull_WhenUpdateCloudBlockBlob_ThenThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => _blobWrapper.UpdateCloudBlockBlob(cloudBlockBlob: null));
        }

        [InlineData(StorageAddress + "/blob2")]
        [InlineData("https://wrongbase/" + ContainerId + BlobIdWithPrefix)]
        [InlineData(StorageAddress + BlobIdWithPrefix)]
        [InlineData(StorageAddress + ContainerId + "/blob2")]
        [Theory]
        public void GivenNewCloudBlockBlobWrongUri_WhenUpdateCloudBlockBlob_ThenThrowsArgumentException(string blobUri)
        {
            var newBlob = new BlockBlobClient(new Uri(blobUri));

            Assert.Throws<ArgumentException>(() => _blobWrapper.UpdateCloudBlockBlob(newBlob));
        }

        [Fact]
        public void GivenNewCloudBlockBlobCorrectUri_WhenUpdateCloudBlockBlob_ThenSuccessfullyUpdated()
        {
            var newBlob = new BlockBlobClient(new Uri(StorageAddress + ContainerId + BlobIdWithPrefix));

            _blobWrapper.UpdateCloudBlockBlob(newBlob);
        }
    }
}
