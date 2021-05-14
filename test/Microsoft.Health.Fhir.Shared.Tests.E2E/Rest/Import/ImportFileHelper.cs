// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;

namespace Microsoft.Health.Fhir.Core.Rest.Import
{
    public static class ImportFileHelper
    {
        public static async Task<(Uri location, string etag)> UploadFileAsync(string content, CloudStorageAccount cloudAccount)
        {
            string blobName = Guid.NewGuid().ToString("N");
            string containerName = Guid.NewGuid().ToString("N");

            CloudBlobClient blobClient = cloudAccount.CreateCloudBlobClient();
            CloudBlobContainer container = blobClient.GetContainerReference(containerName);
            await container.CreateIfNotExistsAsync();

            CloudBlockBlob blob = container.GetBlockBlobReference(blobName);
            await blob.UploadTextAsync(content);

            CloudBlob cloudBlob = container.GetBlobReference(blobName);
            return (cloudBlob.Uri, cloudBlob.Properties.ETag);
        }
    }
}
