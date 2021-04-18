// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Azure.IntegrationDataStore;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Xunit;

namespace Microsoft.Health.Fhir.Azure.UnitTests.IntegrationDataStore
{
    public class AzureBlobIntegrationDataStoreClientTests
    {
        [Fact]
        public async Task GivenTextFileOnBlob_WhenDownloadContent_ContentShouldBeSame()
        {
            IIntegrationDataStoreClientInitilizer<CloudBlobClient> initializer = GetClientInitializer();
            CloudBlobClient client = await initializer.GetAuthorizedClientAsync(CancellationToken.None);

            string containerName = Guid.NewGuid().ToString("N");
            string blobName = Guid.NewGuid().ToString("N");

            var container = client.GetContainerReference(containerName);
            await container.CreateAsync();
            try
            {
                using MemoryStream sourceStream = new MemoryStream();
                using StreamWriter writer = new StreamWriter(sourceStream);

                int lineNumber = (1024 * 1024) + 3;
                while (lineNumber-- > 0)
                {
                    await writer.WriteLineAsync(Guid.NewGuid().ToString("N"));
                }

                await writer.FlushAsync();

                var blob = container.GetBlockBlobReference(blobName);
                sourceStream.Position = 0;
                blob.UploadFromStream(sourceStream);

                sourceStream.Position = 0;
                AzureBlobIntegrationDataStoreClient blobClient = new AzureBlobIntegrationDataStoreClient(initializer, new NullLogger<AzureBlobIntegrationDataStoreClient>());
                using Stream targetStream = blobClient.DownloadResource(blob.Uri, 0, CancellationToken.None);
                using StreamReader sourceReader = new StreamReader(sourceStream);
                using StreamReader targetReader = new StreamReader(targetStream);

                while (!sourceReader.EndOfStream)
                {
                    Assert.Equal(sourceReader.ReadLine(), targetReader.ReadLine());
                }
            }
            finally
            {
                await container.DeleteIfExistsAsync();
            }
        }

        [Fact]
        public async Task GivenTextFileOnBlob_WhenLoadFromMiddle_ContentShouldBeSame()
        {
            IIntegrationDataStoreClientInitilizer<CloudBlobClient> initializer = GetClientInitializer();
            CloudBlobClient client = await initializer.GetAuthorizedClientAsync(CancellationToken.None);

            string containerName = Guid.NewGuid().ToString("N");
            string blobName = Guid.NewGuid().ToString("N");

            var container = client.GetContainerReference(containerName);
            await container.CreateAsync();
            try
            {
                using MemoryStream sourceStream = new MemoryStream();
                using StreamWriter writer = new StreamWriter(sourceStream);

                int lineNumber = (1024 * 1024) + 3;
                while (lineNumber-- > 0)
                {
                    await writer.WriteLineAsync(Guid.NewGuid().ToString("N"));
                }

                await writer.FlushAsync();

                var blob = container.GetBlockBlobReference(blobName);
                sourceStream.Position = 0;
                blob.UploadFromStream(sourceStream);

                long startPosition = 2021;
                sourceStream.Position = startPosition;
                AzureBlobIntegrationDataStoreClient blobClient = new AzureBlobIntegrationDataStoreClient(initializer, new NullLogger<AzureBlobIntegrationDataStoreClient>());
                using Stream targetStream = blobClient.DownloadResource(blob.Uri, startPosition, CancellationToken.None);
                using StreamReader sourceReader = new StreamReader(sourceStream);
                using StreamReader targetReader = new StreamReader(targetStream);

                while (!sourceReader.EndOfStream)
                {
                    Assert.Equal(sourceReader.ReadLine(), targetReader.ReadLine());
                }
            }
            finally
            {
                await container.DeleteIfExistsAsync();
            }
        }

        [Fact]
        public async Task GivenBlobUri_WhenCreateContainer_ContainerShouldBeCreated()
        {
            IIntegrationDataStoreClientInitilizer<CloudBlobClient> initializer = GetClientInitializer();
            CloudBlobClient client = await initializer.GetAuthorizedClientAsync(CancellationToken.None);

            string containerName = Guid.NewGuid().ToString("N");
            string blobName = Guid.NewGuid().ToString("N");

            Uri blobUri = new Uri(Path.Combine(client.StorageUri.PrimaryUri.ToString(), $"{containerName}/{blobName}"));

            try
            {
                AzureBlobIntegrationDataStoreClient blobClient = new AzureBlobIntegrationDataStoreClient(initializer, new NullLogger<AzureBlobIntegrationDataStoreClient>());
                await blobClient.PrepareResourceAsync(blobUri, CancellationToken.None);
                Assert.True(await client.GetContainerReference(containerName).ExistsAsync());
                await blobClient.PrepareResourceAsync(blobUri, CancellationToken.None);
            }
            finally
            {
                var container = client.GetContainerReference(containerName);
                await container.DeleteIfExistsAsync();
            }
        }

        [Fact]
        public async Task GivenTextFileOnBlob_WhenLoadFromMiddle_ContentShouldBeSame1()
        {
            IIntegrationDataStoreClientInitilizer<CloudBlobClient> initializer = GetClientInitializer();
            CloudBlobClient client = await initializer.GetAuthorizedClientAsync(CancellationToken.None);

            string containerName = Guid.NewGuid().ToString("N");
            string blobName = Guid.NewGuid().ToString("N");

            Uri blobUri = new Uri(Path.Combine(client.StorageUri.PrimaryUri.ToString(), $"{containerName}/{blobName}"));

            try
            {
                AzureBlobIntegrationDataStoreClient blobClient = new AzureBlobIntegrationDataStoreClient(initializer, new NullLogger<AzureBlobIntegrationDataStoreClient>());
                await blobClient.PrepareResourceAsync(blobUri, CancellationToken.None);

                long count = 30;
                List<long> blockIds = new List<long>();
                for (long i = 0; i < count; ++i)
                {
                    using Stream input = new MemoryStream(Encoding.UTF8.GetBytes(i.ToString() + "\r\n"));
                    await blobClient.UploadPartDataAsync(blobUri, input, i, CancellationToken.None);
                    blockIds.Add(i);

                    await blobClient.CommitDataAsync(blobUri, blockIds.ToArray(), CancellationToken.None);
                }

                ICloudBlob output = await client.GetBlobReferenceFromServerAsync(blobUri);
                using Stream outputStream = new MemoryStream();
                await output.DownloadToStreamAsync(outputStream);
                outputStream.Position = 0;
                using StreamReader reader = new StreamReader(outputStream);

                long currentLine = 0;
                string content = null;

                while ((content = await reader.ReadLineAsync()) != null)
                {
                    Assert.Equal(currentLine.ToString(), content);
                    currentLine++;
                }

                Assert.Equal(count, currentLine);
            }
            finally
            {
                var container = client.GetContainerReference(containerName);
                await container.DeleteIfExistsAsync();
            }
        }

        private static IIntegrationDataStoreClientInitilizer<CloudBlobClient> GetClientInitializer()
        {
            IntegrationDataStoreConfiguration configuration = new IntegrationDataStoreConfiguration()
            {
                StorageAccountConnection = Environment.GetEnvironmentVariable("IntegrationStore:ConnectionString") ?? "UseDevelopmentStorage=true",
            };
            AzureConnectionStringClientInitializerV2 initializer = new AzureConnectionStringClientInitializerV2(Options.Create(configuration), new NullLogger<AzureConnectionStringClientInitializerV2>());
            return initializer;
        }
    }
}
