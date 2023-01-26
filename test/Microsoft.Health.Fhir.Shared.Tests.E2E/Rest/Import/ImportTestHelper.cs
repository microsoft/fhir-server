// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Health.Fhir.Api.Features.Operations.Import;
using Microsoft.Health.Fhir.Client;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Operations.Import.Models;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Xunit;
using Resource = Hl7.Fhir.Model.Resource;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Import
{
    public static class ImportTestHelper
    {
        private static readonly FhirJsonSerializer _fhirJsonSerializer = new FhirJsonSerializer();

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
            VerifyBundle(result, resources);
        }

        public static void VerifyBundle(Bundle result, params Resource[] resources)
        {
            Assert.Equal(resources.Length, result.Entry.Count);

            foreach (Resource resultResource in result.Entry.Select(e => e.Resource))
            {
                Assert.Equal("1", resultResource.VersionId);
                Assert.Contains(resources, expectedResource => expectedResource.Id.Equals(resultResource.Id));
            }
        }

        public static async Task<TResource[]> ImportToServerAsync<TResource>(TestFhirClient testFhirClient, CloudStorageAccount cloudStorageAccount, params Action<TResource>[] resourceCustomizer)
            where TResource : Resource, new()
        {
            TResource[] resources = new TResource[resourceCustomizer.Length];

            for (int i = 0; i < resources.Length; i++)
            {
                TResource resource = new TResource();

                resourceCustomizer[i](resource);
                resources[i] = resource;
                resources[i].Id = Guid.NewGuid().ToString("N");
            }

            await ImportToServerAsync(testFhirClient, cloudStorageAccount, resources);

            return resources;
        }

        public static async Task ImportToServerAsync(TestFhirClient testFhirClient, CloudStorageAccount cloudStorageAccount, params Resource[] resources)
        {
            Dictionary<string, StringBuilder> contentBuilders = new Dictionary<string, StringBuilder>();

            foreach (Resource resource in resources)
            {
                string resourceType = resource.TypeName.ToString();
                if (!contentBuilders.ContainsKey(resourceType))
                {
                    contentBuilders[resourceType] = new StringBuilder();
                }

                contentBuilders[resourceType].AppendLine(_fhirJsonSerializer.SerializeToString(resource));
            }

            var inputFiles = new List<InputResource>();
            foreach ((string key, StringBuilder builder) in contentBuilders)
            {
                (Uri location, string etag) = await ImportTestHelper.UploadFileAsync(builder.ToString(), cloudStorageAccount);
                inputFiles.Add(new InputResource()
                {
                    Etag = etag,
                    Url = location,
                    Type = key,
                });
            }

            var request = new ImportRequest()
            {
                InputFormat = "application/fhir+ndjson",
                InputSource = new Uri("https://other-server.example.org"),
                StorageDetail = new ImportRequestStorageDetail() { Type = "azure-blob" },
                Input = inputFiles,
            };

            await ImportCheckAsync(testFhirClient, request);
        }

        public static async Task<Uri> CreateImportTaskAsync(TestFhirClient testFhirClient, ImportRequest request)
        {
            while (true)
            {
                try
                {
                    request.Mode = ImportConstants.InitialLoadMode;
                    request.Force = true;
                    Uri checkLocation = await testFhirClient.ImportAsync(request.ToParameters());
                    return checkLocation;
                }
                catch (FhirClientException fhirException)
                {
                    if (!HttpStatusCode.Conflict.Equals(fhirException.StatusCode))
                    {
                        throw;
                    }

                    await Task.Delay(TimeSpan.FromSeconds(5));
                }
            }
        }

        public static T AddTestTag<T>(this T input, string tag)
            where T : Resource
        {
            input.Meta = new Meta();
            input.Meta.Tag.Add(new Coding("http://e2e-test", tag));

            return input;
        }

        private static async Task ImportCheckAsync(TestFhirClient testFhirClient, ImportRequest request)
        {
            Uri checkLocation = await CreateImportTaskAsync(testFhirClient, request);

            while ((await testFhirClient.CheckImportAsync(checkLocation, CancellationToken.None)).StatusCode == System.Net.HttpStatusCode.Accepted)
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
        }
    }
}
