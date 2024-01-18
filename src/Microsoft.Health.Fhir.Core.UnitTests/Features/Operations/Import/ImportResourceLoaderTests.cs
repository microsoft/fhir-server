// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Import
{
    [Trait(Traits.OwningTeam, OwningTeam.FhirImport)]
    [Trait(Traits.Category, Categories.Import)]
    public class ImportResourceLoaderTests
    {
        [Fact]
        public async Task GivenResourceLoader_WhenLoadResources_ThenAllResoruceShouldBeLoad()
        {
            await VerifyResourceLoaderAsync(1234, 21, 0);
        }

        [Fact]
        public async Task GivenResourceLoader_WhenLoadResourcesCountEqualsBatchSize_ThenAllResoruceShouldBeLoad()
        {
            await VerifyResourceLoaderAsync(21, 21, 0);
        }

        [Fact]
        public async Task GivenResourceLoader_WhenLoadResourcesCountLessThanBatchSize_ThenAllResoruceShouldBeLoad()
        {
            await VerifyResourceLoaderAsync(1, 21, 0);
        }

        [Fact]
        public async Task GivenResourceLoader_WhenLoadResourcesWithParseException_ThenAllResoruceShouldBeLoadAndErrorShouldBeReturned()
        {
            string errorMessage = "error";
            using MemoryStream stream = new MemoryStream();
            using StreamWriter writer = new StreamWriter(stream);
            await writer.WriteLineAsync("test");
            await writer.FlushAsync();

            stream.Position = 0;

            IIntegrationDataStoreClient integrationDataStoreClient = Substitute.For<IIntegrationDataStoreClient>();
            integrationDataStoreClient.DownloadResource(Arg.Any<Uri>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).ReturnsForAnyArgs(stream);
            integrationDataStoreClient.TryAcquireLeaseAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).ReturnsForAnyArgs(string.Empty);

            IImportResourceParser importResourceParser = Substitute.For<IImportResourceParser>();
            importResourceParser.Parse(Arg.Any<long>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<ImportMode>())
                .Returns(callInfo =>
                {
                    throw new InvalidOperationException(errorMessage);
                });

            IImportErrorSerializer serializer = Substitute.For<IImportErrorSerializer>();
            serializer.Serialize(Arg.Any<long>(), Arg.Any<Exception>(), Arg.Any<long>())
                .Returns(callInfo =>
                {
                    Exception ex = (Exception)callInfo[1];
                    return ex.Message;
                });

            var loader = new ImportResourceLoader(integrationDataStoreClient, importResourceParser, serializer, NullLogger<ImportResourceLoader>.Instance);

            (Channel<ImportResource> outputChannel, Task importTask) = loader.LoadResources("http://dummy", 0, (int)1e9, null, ImportMode.InitialLoad, CancellationToken.None);

            int errorCount = 0;
            await foreach (ImportResource resource in outputChannel.Reader.ReadAllAsync())
            {
                Assert.Equal(errorMessage, resource.ImportError);
                ++errorCount;
            }

            await importTask;

            Assert.Equal(1, errorCount);
        }

        [Fact]
        public async Task GivenResourceLoader_WhenLoadResourcesWithDifferentResourceType_ThenResourcesWithDifferentTypeShouldBeSkipped()
        {
            string errorMessage = "Resource type not match.";
            using MemoryStream stream = new MemoryStream();
            using StreamWriter writer = new StreamWriter(stream);
            await writer.WriteLineAsync("test");
            await writer.FlushAsync();

            stream.Position = 0;

            IIntegrationDataStoreClient integrationDataStoreClient = Substitute.For<IIntegrationDataStoreClient>();
            integrationDataStoreClient.DownloadResource(Arg.Any<Uri>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).ReturnsForAnyArgs(stream);
            integrationDataStoreClient.TryAcquireLeaseAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).ReturnsForAnyArgs(string.Empty);

            IImportResourceParser importResourceParser = Substitute.For<IImportResourceParser>();
            importResourceParser.Parse(Arg.Any<long>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<ImportMode>())
                .Returns(callInfo =>
                {
                    ImportResource importResource = new ImportResource(null);
                    return importResource;
                });

            IImportErrorSerializer serializer = Substitute.For<IImportErrorSerializer>();
            serializer.Serialize(Arg.Any<long>(), Arg.Any<Exception>(), Arg.Any<long>())
                .Returns(callInfo =>
                {
                    Exception ex = (Exception)callInfo[1];
                    return ex.Message;
                });

            Func<long, long> idGenerator = (i) => i;
            ImportResourceLoader loader = new ImportResourceLoader(integrationDataStoreClient, importResourceParser, serializer, NullLogger<ImportResourceLoader>.Instance);

            (Channel<ImportResource> outputChannel, Task importTask) = loader.LoadResources("http://dummy", 0, (int)1e9, "DummyType", ImportMode.InitialLoad, CancellationToken.None);

            int errorCount = 0;
            await foreach (ImportResource resource in outputChannel.Reader.ReadAllAsync())
            {
                Assert.Equal(errorMessage, resource.ImportError);
                ++errorCount;
            }

            await importTask;

            Assert.Equal(1, errorCount);
        }

        [Fact]
        public async Task GivenResourceLoader_WhenCancelLoadTask_ThenDataLoadTaskShouldBeCanceled()
        {
            string errorMessage = "error";
            using MemoryStream stream = new MemoryStream();
            using StreamWriter writer = new StreamWriter(stream);
            await writer.WriteLineAsync("test");
            await writer.WriteLineAsync("test");
            await writer.WriteLineAsync("test");
            await writer.WriteLineAsync("test");
            await writer.FlushAsync();

            stream.Position = 0;

            AutoResetEvent resetEvent1 = new AutoResetEvent(false);
            ManualResetEvent resetEvent2 = new ManualResetEvent(false);

            IIntegrationDataStoreClient integrationDataStoreClient = Substitute.For<IIntegrationDataStoreClient>();
            integrationDataStoreClient.DownloadResource(Arg.Any<Uri>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).ReturnsForAnyArgs(stream);
            integrationDataStoreClient.TryAcquireLeaseAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).ReturnsForAnyArgs(string.Empty);

            IImportResourceParser importResourceParser = Substitute.For<IImportResourceParser>();
            importResourceParser.Parse(Arg.Any<long>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<ImportMode>())
                .Returns(callInfo =>
                {
                    resetEvent1.Set();
                    resetEvent2.WaitOne();

                    throw new InvalidCastException(errorMessage);
                });

            IImportErrorSerializer serializer = Substitute.For<IImportErrorSerializer>();
            serializer.Serialize(Arg.Any<long>(), Arg.Any<Exception>(), Arg.Any<long>())
                .Returns(callInfo =>
                {
                    Exception ex = (Exception)callInfo[1];
                    return ex.Message;
                });

            Func<long, long> idGenerator = (i) => i;
            ImportResourceLoader loader = new ImportResourceLoader(integrationDataStoreClient, importResourceParser, serializer, NullLogger<ImportResourceLoader>.Instance);

            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
            (Channel<ImportResource> outputChannel, Task importTask) = loader.LoadResources("http://dummy", 0, (int)1e9, null, ImportMode.InitialLoad, cancellationTokenSource.Token);

            resetEvent1.WaitOne();
            cancellationTokenSource.Cancel();
            resetEvent2.Set();

            await foreach (ImportResource resource in outputChannel.Reader.ReadAllAsync())
            {
                // do nothing.
            }

            try
            {
                await importTask;
                throw new InvalidOperationException();
            }
            catch (TaskCanceledException)
            {
                // Expected error
            }
            catch (OperationCanceledException)
            {
                // Expected error
            }
        }

        [Theory]
        [InlineData(0, 240, 5)] // Exactly the first four resources and the /r/n after.
        [InlineData(0, 299, 6)] // Exactly the first five resources and the /r/n after. Note: resource 5 has one more byte (57) vs the others (56). Index 299 is just after the } of the fifth resource.
        [InlineData(58, 58, 1)] // Start with the /n of the first resource, and load through the end of the } on the second resource.
        [InlineData(60, 56, 0)] // Start with the { of the second resource, and load through the end of the } on the second resource.
        [InlineData(40, 78, 2)] // Start in the middle of the first resource, and load through the \r on the second resource.
        public async Task GivenResourceLoader_WhenLoadingBytesThatFallsOnNewLine_ProperNumberOfResourcesAreLoaded(int startIndex, int bytesToLoad, int expectedResourceCount)
        {
            // Each of these resources are 56 bytes, adding \r\n makes each line 60 bytes.
            string[] resources = [
                @"{ ""resourceType"": ""Observation"", ""meta"": { ""id"": ""6"" } }",
                @"{ ""resourceType"": ""Observation"", ""meta"": { ""id"": ""7"" } }",
                @"{ ""resourceType"": ""Observation"", ""meta"": { ""id"": ""8"" } }",
                @"{ ""resourceType"": ""Observation"", ""meta"": { ""id"": ""9"" } }",
                @"{ ""resourceType"": ""Observation"", ""meta"": { ""id"": ""10"" } }",
                @"{ ""resourceType"": ""Observation"", ""meta"": { ""id"": ""11"" } }",
                @"{ ""resourceType"": ""Observation"", ""meta"": { ""id"": ""12"" } }",
            ];

            using MemoryStream stream = new();
            using StreamWriter writer = new(stream);

            for (int i = 0; i < resources.Length; ++i)
            {
                await writer.WriteAsync(resources[i]);
                await writer.WriteAsync("\r\n");
            }

            await writer.FlushAsync();
            stream.Position = startIndex == 0 ? 0 : startIndex - 1;

            IIntegrationDataStoreClient integrationDataStoreClient = Substitute.For<IIntegrationDataStoreClient>();
            integrationDataStoreClient.DownloadResource(Arg.Any<Uri>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).ReturnsForAnyArgs(stream);
            integrationDataStoreClient.TryAcquireLeaseAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).ReturnsForAnyArgs(string.Empty);

            IImportResourceParser importResourceParser = Substitute.For<IImportResourceParser>();
            importResourceParser.Parse(Arg.Any<long>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<ImportMode>())
                .Returns(_ => { return Substitute.For<ImportResource>(); });

            IImportErrorSerializer serializer = Substitute.For<IImportErrorSerializer>();

            ImportResourceLoader loader = new ImportResourceLoader(integrationDataStoreClient, importResourceParser, serializer, NullLogger<ImportResourceLoader>.Instance);

            // should be 120 bytes
            (Channel<ImportResource> outputChannel, Task importTask) = loader.LoadResources("http://dummy", startIndex, bytesToLoad, null, ImportMode.InitialLoad, CancellationToken.None);

            long actualResourceCount = 0;
            await foreach (ImportResource resource in outputChannel.Reader.ReadAllAsync())
            {
                actualResourceCount++;
            }

            await importTask;

            Assert.Equal(expectedResourceCount, actualResourceCount);
        }

        [Theory]
        [InlineData(0, 115, 3)] // Start at the first resource and ends at \n of second resource
        [InlineData(0, 229, 4)] // Start at the first resource and ends at } right before \n of 4th line
        [InlineData(59, 56, 0)] // Start at second resource and ends at } of the same resource.
        [InlineData(59, 59, 1)] // Start at second resource and ends partial line of resource 3.
        [InlineData(40, 76, 2)] // Start in the middle of the first resource and ends at \n of second resource
        public async Task GivenResourceLoader_WhenLoadingBytesThatFallsOnDifferentNewLineChars_ProperNumberOfResourcesAreLoaded(int startIndex, int bytesToLoad, int expectedResourceCount)
        {
            // Each of these resources are 56 bytes, adding \r\n makes each line 60 bytes.
            string[] resources = [
                @"{ ""resourceType"": ""Observation"", ""meta"": { ""id"": ""6"" } }",
                @"{ ""resourceType"": ""Observation"", ""meta"": { ""id"": ""7"" } }",
                @"{ ""resourceType"": ""Observation"", ""meta"": { ""id"": ""8"" } }",
                @"{ ""resourceType"": ""Observation"", ""meta"": { ""id"": ""9"" } }",
                @"{ ""resourceType"": ""Observation"", ""meta"": { ""id"": ""10"" } }",
                @"{ ""resourceType"": ""Observation"", ""meta"": { ""id"": ""11"" } }",
                @"{ ""resourceType"": ""Observation"", ""meta"": { ""id"": ""12"" } }",
            ];

            using MemoryStream stream = new();
            using StreamWriter writer = new(stream);

            for (int i = 0; i < resources.Length; i++)
            {
                await writer.WriteAsync(resources[i]);
                if (i % 2 == 0)
                {
                    await writer.WriteAsync("\r\n");
                }
                else
                {
                    await writer.WriteAsync("\n");
                }
            }

            await writer.FlushAsync();
            stream.Position = startIndex == 0 ? 0 : startIndex - 1;

            IIntegrationDataStoreClient integrationDataStoreClient = Substitute.For<IIntegrationDataStoreClient>();
            integrationDataStoreClient.DownloadResource(Arg.Any<Uri>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).ReturnsForAnyArgs(stream);
            integrationDataStoreClient.TryAcquireLeaseAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).ReturnsForAnyArgs(string.Empty);

            IImportResourceParser importResourceParser = Substitute.For<IImportResourceParser>();
            importResourceParser.Parse(Arg.Any<long>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<ImportMode>())
                .Returns(_ => { return Substitute.For<ImportResource>(); });

            IImportErrorSerializer serializer = Substitute.For<IImportErrorSerializer>();

            ImportResourceLoader loader = new ImportResourceLoader(integrationDataStoreClient, importResourceParser, serializer, NullLogger<ImportResourceLoader>.Instance);

            // should be 120 bytes
            (Channel<ImportResource> outputChannel, Task importTask) = loader.LoadResources("http://dummy", startIndex, bytesToLoad, null, ImportMode.InitialLoad, CancellationToken.None);

            long actualResourceCount = 0;
            await foreach (ImportResource resource in outputChannel.Reader.ReadAllAsync())
            {
                actualResourceCount++;
            }

            await importTask;

            Assert.Equal(expectedResourceCount, actualResourceCount);
        }

        private async Task VerifyResourceLoaderAsync(int resourcCount, int batchSize, long startIndex)
        {
            long startId = 1;
            List<string> inputStrings = new List<string>();
            using MemoryStream stream = new MemoryStream();
            using StreamWriter writer = new StreamWriter(stream);
            for (int i = 0; i < resourcCount; ++i)
            {
                string content = (i + startId).ToString();
                inputStrings.Add(content);
                await writer.WriteLineAsync(content);
            }

            await writer.FlushAsync();
            stream.Position = 0;

            IIntegrationDataStoreClient integrationDataStoreClient = Substitute.For<IIntegrationDataStoreClient>();
            integrationDataStoreClient.DownloadResource(Arg.Any<Uri>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).ReturnsForAnyArgs(stream);
            integrationDataStoreClient.TryAcquireLeaseAsync(Arg.Any<Uri>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).ReturnsForAnyArgs(string.Empty);

            IImportResourceParser importResourceParser = Substitute.For<IImportResourceParser>();
            importResourceParser.Parse(Arg.Any<long>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<ImportMode>())
                .Returns(callInfo =>
                {
                    long index = (long)callInfo[0];
                    string content = (string)callInfo[3];
                    ResourceWrapper resourceWrapper = new ResourceWrapper(
                            content,
                            "0",
                            "Dummy",
                            new RawResource(content, Core.Models.FhirResourceFormat.Json, true),
                            new ResourceRequest("POST"),
                            DateTimeOffset.UtcNow,
                            false,
                            null,
                            null,
                            null,
                            "SearchParam");
                    return new ImportResource(index, 0, 0, false, false, false, resourceWrapper);
                });

            IImportErrorSerializer serializer = Substitute.For<IImportErrorSerializer>();

            ImportResourceLoader loader = new ImportResourceLoader(integrationDataStoreClient, importResourceParser, serializer, NullLogger<ImportResourceLoader>.Instance);

            (Channel<ImportResource> outputChannel, Task importTask) = loader.LoadResources("http://dummy", 0, (int)1e9, null, ImportMode.InitialLoad, CancellationToken.None);

            long currentIndex = startIndex;
            await foreach (ImportResource resource in outputChannel.Reader.ReadAllAsync())
            {
                string content = (currentIndex++).ToString();
            }

            await importTask;

            Assert.Equal(resourcCount, currentIndex);
        }
    }
}
