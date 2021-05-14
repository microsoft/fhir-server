// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Xunit;
using Resource = Hl7.Fhir.Model.Resource;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Import
{
    public static class ImportTestHelper
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

        public static async Task<string> DownloadFileAsync(string location, CloudStorageAccount cloudAccount)
        {
            CloudBlobClient blobClient = cloudAccount.CreateCloudBlobClient();
            ICloudBlob container = blobClient.GetBlobReferenceFromServer(new Uri(location));

            using MemoryStream stream = new MemoryStream();
            await container.DownloadToStreamAsync(stream, CancellationToken.None);

            stream.Position = 0;
            using StreamReader reader = new StreamReader(stream);
            return await reader.ReadToEndAsync();
        }

        public static async Task VerifySearchResultAsync(TestFhirClient client, string query, params Resource[] resources)
        {
            Bundle result = await client.SearchAsync(query);
            Assert.Equal(resources.Length, result.Entry.Count);

            foreach (Resource resultResource in result.Entry.Select(e => e.Resource))
            {
                Assert.Contains(resources, expectedResource => expectedResource.Id.Equals(resultResource.Id));
            }
        }
    }
}
