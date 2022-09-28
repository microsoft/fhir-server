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
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Azure.UnitTests.IntegrationDataStore
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class AzureBlobIntegrationDataStoreClientTests
    {
        [Fact(Skip = "Local tests need emulator.")]
        public async Task GivenTextFileOnBlob_WhenDownloadContent_ThenContentShouldBeSame()
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
                AzureBlobIntegrationDataStoreClient blobClient = new AzureBlobIntegrationDataStoreClient(initializer, GetIntegrationDataStoreConfigurationOption(), new NullLogger<AzureBlobIntegrationDataStoreClient>());
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

        [Fact(Skip = "Local tests need emulator.")]
        public async Task GivenTextFileOnBlob_WhenLoadFromMiddle_ThenContentShouldBeSame()
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
                AzureBlobIntegrationDataStoreClient blobClient = new AzureBlobIntegrationDataStoreClient(initializer, GetIntegrationDataStoreConfigurationOption(), new NullLogger<AzureBlobIntegrationDataStoreClient>());
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

        [Fact(Skip = "Local tests need emulator.")]
        public async Task GivenBlobUri_WhenCreateContainer_ThenContainerShouldBeCreated()
        {
            IIntegrationDataStoreClientInitilizer<CloudBlobClient> initializer = GetClientInitializer();
            CloudBlobClient client = await initializer.GetAuthorizedClientAsync(CancellationToken.None);

            string containerName = Guid.NewGuid().ToString("N");
            string blobName = Guid.NewGuid().ToString("N");

            Uri blobUri = new Uri(Path.Combine(client.StorageUri.PrimaryUri.ToString(), $"{containerName}/{blobName}"));

            try
            {
                AzureBlobIntegrationDataStoreClient blobClient = new AzureBlobIntegrationDataStoreClient(initializer, GetIntegrationDataStoreConfigurationOption(), new NullLogger<AzureBlobIntegrationDataStoreClient>());
                Uri fileUri = await blobClient.PrepareResourceAsync(containerName, blobName, CancellationToken.None);
                Assert.True(await client.GetContainerReference(containerName).ExistsAsync());
                Assert.Equal(blobUri, fileUri);

                await blobClient.PrepareResourceAsync(containerName, blobName, CancellationToken.None);
            }
            finally
            {
                var container = client.GetContainerReference(containerName);
                await container.DeleteIfExistsAsync();
            }
        }

        [Fact(Skip = "Local tests need emulator.")]
        public async Task GivenABlob_WhenGetProperties_ThenProtertiesShouldBeReturned()
        {
            IIntegrationDataStoreClientInitilizer<CloudBlobClient> initializer = GetClientInitializer();
            CloudBlobClient client = await initializer.GetAuthorizedClientAsync(CancellationToken.None);

            string containerName = Guid.NewGuid().ToString("N");
            string blobName = Guid.NewGuid().ToString("N");

            Uri blobUri = new Uri(Path.Combine(client.StorageUri.PrimaryUri.ToString(), $"{containerName}/{blobName}"));

            var container = client.GetContainerReference(containerName);
            await container.CreateAsync();
            try
            {
                using MemoryStream sourceStream = new MemoryStream();
                using StreamWriter writer = new StreamWriter(sourceStream);

                await writer.WriteLineAsync(Guid.NewGuid().ToString("N"));
                await writer.FlushAsync();

                var blob = container.GetBlockBlobReference(blobName);
                sourceStream.Position = 0;
                blob.UploadFromStream(sourceStream);

                AzureBlobIntegrationDataStoreClient blobClient = new AzureBlobIntegrationDataStoreClient(initializer, GetIntegrationDataStoreConfigurationOption(), new NullLogger<AzureBlobIntegrationDataStoreClient>());
                Dictionary<string, object> properties = await blobClient.GetPropertiesAsync(blobUri, CancellationToken.None);
                Assert.True(properties.ContainsKey(IntegrationDataStoreClientConstants.BlobPropertyETag));
                Assert.True(properties.ContainsKey(IntegrationDataStoreClientConstants.BlobPropertyLength));
            }
            finally
            {
                await container.DeleteIfExistsAsync();
            }
        }

        [Fact(Skip = "Local tests need emulator.")]
        public async Task GivenDataStream_WhenUploadToBlob_ThenAllDataShouldBeUploaded()
        {
            IIntegrationDataStoreClientInitilizer<CloudBlobClient> initializer = GetClientInitializer();
            CloudBlobClient client = await initializer.GetAuthorizedClientAsync(CancellationToken.None);

            string containerName = Guid.NewGuid().ToString("N");
            string blobName = Guid.NewGuid().ToString("N");

            Uri blobUri = new Uri(Path.Combine(client.StorageUri.PrimaryUri.ToString(), $"{containerName}/{blobName}"));

            try
            {
                AzureBlobIntegrationDataStoreClient blobClient = new AzureBlobIntegrationDataStoreClient(initializer, GetIntegrationDataStoreConfigurationOption(), new NullLogger<AzureBlobIntegrationDataStoreClient>());
                await blobClient.PrepareResourceAsync(containerName, blobName, CancellationToken.None);

                long count = 30;
                List<string> blockIds = new List<string>();
                for (long i = 0; i < count; ++i)
                {
                    using Stream input = new MemoryStream(Encoding.UTF8.GetBytes(i.ToString() + "\r\n"));
                    string blockId = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
                    await blobClient.UploadBlockAsync(blobUri, input, blockId, CancellationToken.None);
                    blockIds.Add(blockId);
                }

                await blobClient.CommitAsync(blobUri, blockIds.ToArray(), CancellationToken.None);

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

        [Fact(Skip = "Local tests need emulator.")]
        public async Task GivenDataStream_WhenAppendToBlob_ThenDataShouldBeAppended()
        {
            IIntegrationDataStoreClientInitilizer<CloudBlobClient> initializer = GetClientInitializer();
            CloudBlobClient client = await initializer.GetAuthorizedClientAsync(CancellationToken.None);

            string containerName = Guid.NewGuid().ToString("N");
            string blobName = Guid.NewGuid().ToString("N");

            Uri blobUri = new Uri(Path.Combine(client.StorageUri.PrimaryUri.ToString(), $"{containerName}/{blobName}"));

            try
            {
                AzureBlobIntegrationDataStoreClient blobClient = new AzureBlobIntegrationDataStoreClient(initializer, GetIntegrationDataStoreConfigurationOption(), new NullLogger<AzureBlobIntegrationDataStoreClient>());
                await blobClient.PrepareResourceAsync(containerName, blobName, CancellationToken.None);

                long count = 30;
                List<string> blockIds = new List<string>();
                for (long i = 0; i < count; ++i)
                {
                    using Stream input = new MemoryStream(Encoding.UTF8.GetBytes(i.ToString() + "\r\n"));
                    string blockId = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
                    await blobClient.UploadBlockAsync(blobUri, input, blockId, CancellationToken.None);
                    blockIds.Add(blockId);
                }

                await blobClient.CommitAsync(blobUri, blockIds.ToArray(), CancellationToken.None);

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

        [Fact(Skip = "Local tests need emulator.")]
        public async Task GivenStorageBlob_WhenAcquireLease_ThenLeaseIdShouldBeReturned()
        {
            IIntegrationDataStoreClientInitilizer<CloudBlobClient> initializer = GetClientInitializer();
            CloudBlobClient client = await initializer.GetAuthorizedClientAsync(CancellationToken.None);

            string containerName = Guid.NewGuid().ToString("N");
            string blobName = Guid.NewGuid().ToString("N");

            Uri blobUri = new Uri(Path.Combine(client.StorageUri.PrimaryUri.ToString(), $"{containerName}/{blobName}"));

            try
            {
                AzureBlobIntegrationDataStoreClient blobClient = new AzureBlobIntegrationDataStoreClient(initializer, GetIntegrationDataStoreConfigurationOption(), new NullLogger<AzureBlobIntegrationDataStoreClient>());
                await blobClient.PrepareResourceAsync(containerName, blobName, CancellationToken.None);

                long count = 30;
                List<string> blockIds = new List<string>();
                for (long i = 0; i < count; ++i)
                {
                    using Stream input = new MemoryStream(Encoding.UTF8.GetBytes(i.ToString() + "\r\n"));
                    string blockId = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
                    await blobClient.UploadBlockAsync(blobUri, input, blockId, CancellationToken.None);
                    blockIds.Add(blockId);
                }

                await blobClient.CommitAsync(blobUri, blockIds.ToArray(), CancellationToken.None);

                string leaseId = await blobClient.TryAcquireLeaseAsync(blobUri, blobName, CancellationToken.None);
                Assert.NotNull(leaseId);
                string nullLeaseId = await blobClient.TryAcquireLeaseAsync(blobUri, "dummy", CancellationToken.None);
                Assert.Null(nullLeaseId);

                await blobClient.TryReleaseLeaseAsync(blobUri, leaseId, CancellationToken.None);
            }
            finally
            {
                var container = client.GetContainerReference(containerName);
                await container.DeleteIfExistsAsync();
            }
        }

        private static IIntegrationDataStoreClientInitilizer<CloudBlobClient> GetClientInitializer()
        {
            return new AzureConnectionStringClientInitializerV2(GetIntegrationDataStoreConfigurationOption(), new NullLogger<AzureConnectionStringClientInitializerV2>());
        }

        private static IOptions<IntegrationDataStoreConfiguration> GetIntegrationDataStoreConfigurationOption()
        {
            return Options.Create(new IntegrationDataStoreConfiguration()
                            {
                                StorageAccountConnection = "UseDevelopmentStorage=true",
                            });
        }
    }
}
