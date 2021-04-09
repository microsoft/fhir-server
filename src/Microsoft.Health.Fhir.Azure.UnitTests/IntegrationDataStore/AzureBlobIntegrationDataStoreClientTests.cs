// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
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

                int lineNumber = 1024 * 1024;
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

                int lineNumber = 1024 * 1024;
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

        private static IIntegrationDataStoreClientInitilizer<CloudBlobClient> GetClientInitializer()
        {
            IntegrationDataStoreConfiguration configuration = new IntegrationDataStoreConfiguration()
            {
                StorageAccountConnection = "UseDevelopmentStorage=true",
            };
            AzureConnectionStringClientInitializerV2 initializer = new AzureConnectionStringClientInitializerV2(Options.Create(configuration), new NullLogger<AzureConnectionStringClientInitializerV2>());
            return initializer;
        }
    }
}
