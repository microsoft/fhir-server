// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Azure.Storage.Blob;
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
        private CloudBlockBlob _cloudBlockBlob;
        private CloudBlobClient _blobClient;
        private CloudBlockBlobWrapper _blobWrapper;

        public CloudBlockBlobWrapperTests()
        {
            _blobUri = new Uri(StorageAddress + ContainerId + BlobIdWithPrefix);
            _blobClient = new CloudBlobClient(new Uri(StorageAddress));
            _cloudBlockBlob = new CloudBlockBlob(_blobUri, _blobClient);
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
            CloudBlockBlob newBlob = new CloudBlockBlob(new Uri(blobUri), _blobClient);

            Assert.Throws<ArgumentException>(() => _blobWrapper.UpdateCloudBlockBlob(newBlob));
        }

        [Fact]
        public void GivenNewCloudBlockBlobCorrectUri_WhenUpdateCloudBlockBlob_ThenSuccessfullyUpdated()
        {
            CloudBlockBlob newBlob = new CloudBlockBlob(new Uri(StorageAddress + ContainerId + BlobIdWithPrefix), _blobClient);

            _blobWrapper.UpdateCloudBlockBlob(newBlob);
        }
    }
}
