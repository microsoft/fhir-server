// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Medino;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Audit;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Features.Operations.Import;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Operations.Import
{
    [Trait(Traits.OwningTeam, OwningTeam.FhirImport)]
    [Trait(Traits.Category, Categories.Import)]
    public class ImportProcessingJobTests
    {
        [Fact]
        public void GivenTextWithDifferentEndOfLines_WhenAccessingByOffsets_AllLinesAreRead()
        {
            var input = $"A123456789{"\n"}B123456789{"\r\n"}C123456789{"\n"}D123456789{"\r\n"}E123456789{"\n"}F123456789{"\r\n"}";
            var blobLength = input.Length;
            for (var bytesToRead = 1; bytesToRead < 100; bytesToRead++)
            {
                var outputLines = 0;
                var outputString = new StringBuilder();
                foreach (var offset in ImportOrchestratorJob.GetOffsets(blobLength, bytesToRead))
                {
                    using var reader = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes(input)));
                    reader.BaseStream.Position = offset;
                    foreach (var line in ImportResourceLoader.ReadLines(offset, bytesToRead, reader))
                    {
                        outputLines++;
                        outputString.Append(line.Line);
                    }
                }

                Assert.Equal(input.Replace("\r\n", string.Empty).Replace("\n", string.Empty), outputString.ToString());
                Assert.Equal(6, outputLines);
            }
        }

        [Fact]
        public void GivenText_WhenAccessingByOffsets_AllLinesAreRead()
        {
            foreach (var endOfLine in new[] { "\n", "\r\n" })
            {
                // this will also check that empty lines are not read
                var input = $"A123456789{endOfLine}B123456789{endOfLine}C123456789{endOfLine}D123456789{endOfLine}{endOfLine}E123456789{endOfLine}";
                var blobLength = 50L + (5 * endOfLine.Length);
                for (var bytesToRead = 1; bytesToRead < 100; bytesToRead++)
                {
                    var outputLines = 0;
                    var outputString = new StringBuilder();
                    foreach (var offset in ImportOrchestratorJob.GetOffsets(blobLength, bytesToRead))
                    {
                        using var reader = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes(input)));
                        reader.BaseStream.Position = offset;
                        foreach (var line in ImportResourceLoader.ReadLines(offset, bytesToRead, reader))
                        {
                            outputLines++;
                            outputString.Append(line.Line);
                        }
                    }

                    Assert.Equal(input.Replace(endOfLine, string.Empty), outputString.ToString());
                    Assert.Equal(5, outputLines);
                }
            }
        }

        [Fact]
        public async Task GivenImportInput_WhenStartFromClean_ThenAllResoruceShouldBeImported()
        {
            ImportProcessingJobDefinition inputData = GetInputData();
            ImportProcessingJobResult result = new ImportProcessingJobResult();
            await VerifyCommonImportAsync(inputData, result);
        }

        [Fact]
        public async Task GivenImportInput_WhenExceptionThrowForLoad_ThenJobExecutionExceptionShouldBeThrown()
        {
            ImportProcessingJobDefinition inputData = GetInputData();
            ImportProcessingJobResult result = new ImportProcessingJobResult();

            IImportResourceLoader loader = Substitute.For<IImportResourceLoader>();
            IImporter importer = Substitute.For<IImporter>();
            IImportErrorStore importErrorStore = Substitute.For<IImportErrorStore>();
            IImportErrorStoreFactory importErrorStoreFactory = Substitute.For<IImportErrorStoreFactory>();
            RequestContextAccessor<IFhirRequestContext> contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            ILoggerFactory loggerFactory = new NullLoggerFactory();
            IMediator mediator = Substitute.For<IMediator>();
            IAuditLogger auditLogger = Substitute.For<IAuditLogger>();
            IQueueClient queueClient = Substitute.For<IQueueClient>();

            loader.LoadResources(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<ImportMode>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    Channel<ImportResource> resourceChannel = Channel.CreateUnbounded<ImportResource>();

                    Task loadTask = Task.Run(async () =>
                    {
                        try
                        {
                            await Task.Delay(TimeSpan.FromSeconds(1));
                            throw new InvalidOperationException();
                        }
                        finally
                        {
                            resourceChannel.Writer.Complete();
                        }
                    });

                    return (resourceChannel, loadTask);
                });

            importer.Import(Arg.Any<Channel<ImportResource>>(), Arg.Any<IImportErrorStore>(), Arg.Any<ImportMode>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    return new ImportProcessingProgress();
                });

            ImportProcessingJob job = new ImportProcessingJob(
                                    mediator,
                                    queueClient,
                                    loader,
                                    importer,
                                    importErrorStoreFactory,
                                    contextAccessor,
                                    loggerFactory,
                                    auditLogger);

            await Assert.ThrowsAsync<JobExecutionException>(() => job.ExecuteAsync(GetJobInfo(inputData, result), CancellationToken.None));
        }

        [Fact]
        public async Task GivenImportInput_WhenOperationWasCancelledExceptionThrow_ThenJobShouldFailed()
        {
            ImportProcessingJobDefinition inputData = GetInputData();
            ImportProcessingJobResult result = new ImportProcessingJobResult();

            IImportResourceLoader loader = Substitute.For<IImportResourceLoader>();
            IImporter importer = Substitute.For<IImporter>();
            IImportErrorStore importErrorStore = Substitute.For<IImportErrorStore>();
            IImportErrorStoreFactory importErrorStoreFactory = Substitute.For<IImportErrorStoreFactory>();
            RequestContextAccessor<IFhirRequestContext> contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            ILoggerFactory loggerFactory = new NullLoggerFactory();
            IMediator mediator = Substitute.For<IMediator>();
            IAuditLogger auditLogger = Substitute.For<IAuditLogger>();
            IQueueClient queueClient = Substitute.For<IQueueClient>();

            importer.Import(Arg.Any<Channel<ImportResource>>(), Arg.Any<IImportErrorStore>(), Arg.Any<ImportMode>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    if (callInfo[2] != null) // always true
                    {
                        throw new OperationCanceledException();
                    }

                    return new ImportProcessingProgress();
                });

            ImportProcessingJob job = new ImportProcessingJob(
                                    mediator,
                                    queueClient,
                                    loader,
                                    importer,
                                    importErrorStoreFactory,
                                    contextAccessor,
                                    loggerFactory,
                                    auditLogger);

            await Assert.ThrowsAsync<JobExecutionException>(() => job.ExecuteAsync(GetJobInfo(inputData, result), CancellationToken.None));
        }

        [Fact]
        public async Task GivenImportInputWithErrorContainerName_WhenExecuted_ThenErrorContainerNameShouldBeUsed()
        {
            // Arrange
            ImportProcessingJobDefinition inputData = GetInputData();
            inputData.ErrorContainerName = "custom-error-container";
            ImportProcessingJobResult result = new ImportProcessingJobResult();

            IImportResourceLoader loader = Substitute.For<IImportResourceLoader>();
            IImporter importer = Substitute.For<IImporter>();
            IImportErrorStore importErrorStore = Substitute.For<IImportErrorStore>();
            IImportErrorStoreFactory importErrorStoreFactory = Substitute.For<IImportErrorStoreFactory>();
            RequestContextAccessor<IFhirRequestContext> contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            ILoggerFactory loggerFactory = new NullLoggerFactory();
            IMediator mediator = Substitute.For<IMediator>();
            IAuditLogger auditLogger = Substitute.For<IAuditLogger>();
            IQueueClient queueClient = Substitute.For<IQueueClient>();

            // Setup the mock loader to return a channel and task
            loader.LoadResources(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<ImportMode>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    Channel<ImportResource> resourceChannel = Channel.CreateUnbounded<ImportResource>();
                    Task loadTask = Task.CompletedTask;
                    resourceChannel.Writer.Complete();
                    return (resourceChannel, loadTask);
                });

            // Setup the import error store
            importErrorStore.ErrorFileLocation.Returns("error-location");

            // Setup the error store factory with custom container
            importErrorStoreFactory.InitializeAsync(
                Arg.Is<string>(container => container == "custom-error-container"),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
                .Returns(importErrorStore);

            importer.Import(Arg.Any<Channel<ImportResource>>(), Arg.Any<IImportErrorStore>(), Arg.Any<ImportMode>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
                .Returns(new ImportProcessingProgress());

            var job = new ImportProcessingJob(
                mediator,
                queueClient,
                loader,
                importer,
                importErrorStoreFactory,
                contextAccessor,
                loggerFactory,
                auditLogger);

            // Act
            await job.ExecuteAsync(GetJobInfo(inputData, result), CancellationToken.None);

            // Assert
            await importErrorStoreFactory.Received(1).InitializeAsync(
                Arg.Is<string>(container => container == "custom-error-container"),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenImportInputWithoutErrorContainerName_WhenExecuted_ThenDefaultErrorContainerShouldBeUsed()
        {
            // Arrange
            ImportProcessingJobDefinition inputData = GetInputData();
            inputData.ErrorContainerName = null; // No custom error container name
            ImportProcessingJobResult result = new ImportProcessingJobResult();

            IImportResourceLoader loader = Substitute.For<IImportResourceLoader>();
            IImporter importer = Substitute.For<IImporter>();
            IImportErrorStore importErrorStore = Substitute.For<IImportErrorStore>();
            IImportErrorStoreFactory importErrorStoreFactory = Substitute.For<IImportErrorStoreFactory>();
            RequestContextAccessor<IFhirRequestContext> contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            ILoggerFactory loggerFactory = new NullLoggerFactory();
            IMediator mediator = Substitute.For<IMediator>();
            IAuditLogger auditLogger = Substitute.For<IAuditLogger>();
            IQueueClient queueClient = Substitute.For<IQueueClient>();

            // Setup the mock loader to return a channel and task
            loader.LoadResources(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<ImportMode>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    Channel<ImportResource> resourceChannel = Channel.CreateUnbounded<ImportResource>();
                    Task loadTask = Task.CompletedTask;
                    resourceChannel.Writer.Complete();
                    return (resourceChannel, loadTask);
                });

            // Setup the import error store
            importErrorStore.ErrorFileLocation.Returns("error-location");

            // Setup the error store factory without custom container (default method)
            importErrorStoreFactory.InitializeAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
                .Returns(importErrorStore);

            importer.Import(Arg.Any<Channel<ImportResource>>(), Arg.Any<IImportErrorStore>(), Arg.Any<ImportMode>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
                .Returns(new ImportProcessingProgress());

            var job = new ImportProcessingJob(
                mediator,
                queueClient,
                loader,
                importer,
                importErrorStoreFactory,
                contextAccessor,
                loggerFactory,
                auditLogger);

            // Act
            await job.ExecuteAsync(GetJobInfo(inputData, result), CancellationToken.None);

            // Assert
            await importErrorStoreFactory.Received(1).InitializeAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>());

            // And verify the overload with container name was NOT called
            await importErrorStoreFactory.DidNotReceive().InitializeAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>());
        }

        private static async Task VerifyCommonImportAsync(ImportProcessingJobDefinition inputData, ImportProcessingJobResult currentResult)
        {
            long succeedCountFromProgress = currentResult.SucceededResources;
            long failedCountFromProgress = currentResult.FailedResources;

            IImportResourceLoader loader = Substitute.For<IImportResourceLoader>();
            IImporter importer = Substitute.For<IImporter>();
            IImportErrorStore importErrorStore = Substitute.For<IImportErrorStore>();
            IImportErrorStoreFactory importErrorStoreFactory = Substitute.For<IImportErrorStoreFactory>();
            RequestContextAccessor<IFhirRequestContext> contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            ILoggerFactory loggerFactory = new NullLoggerFactory();
            IMediator mediator = Substitute.For<IMediator>();
            IAuditLogger auditLogger = Substitute.For<IAuditLogger>();
            IQueueClient queueClient = Substitute.For<IQueueClient>();

            loader.LoadResources(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<ImportMode>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    Channel<ImportResource> resourceChannel = Channel.CreateUnbounded<ImportResource>();

                    Task loadTask = Task.Run(async () =>
                    {
                        var resourceWrapper = new ResourceWrapper(
                            Guid.NewGuid().ToString(),
                            "0",
                            "Patient",
                            new RawResource(Guid.NewGuid().ToString(), Fhir.Core.Models.FhirResourceFormat.Json, true),
                            new ResourceRequest("POST"),
                            DateTimeOffset.UtcNow,
                            false,
                            null,
                            null,
                            null,
                            "SearchParam");

                        await resourceChannel.Writer.WriteAsync(new ImportResource(0, 0, 0, false, false, false, resourceWrapper));
                        await resourceChannel.Writer.WriteAsync(new ImportResource(1, 0, "Error"));
                        resourceChannel.Writer.Complete();
                    });

                    return (resourceChannel, loadTask);
                });

            importer.Import(Arg.Any<Channel<ImportResource>>(), Arg.Any<IImportErrorStore>(), Arg.Any<ImportMode>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
                .Returns(async callInfo =>
                {
                    Channel<ImportResource> resourceChannel = (Channel<ImportResource>)callInfo[0];
                    var progress = new ImportProcessingProgress();
                    await foreach (var resource in resourceChannel.Reader.ReadAllAsync())
                    {
                        if (string.IsNullOrEmpty(resource.ImportError))
                        {
                            progress.SucceededResources++;
                        }
                        else
                        {
                            progress.FailedResources++;
                        }
                    }

                    return progress;
                });

            var job = new ImportProcessingJob(mediator, queueClient, loader, importer, importErrorStoreFactory, contextAccessor, loggerFactory, auditLogger);

            string resultString = await job.ExecuteAsync(GetJobInfo(inputData, currentResult), CancellationToken.None);
            ImportProcessingJobResult result = JsonConvert.DeserializeObject<ImportProcessingJobResult>(resultString);
            Assert.Equal(1 + failedCountFromProgress, result.FailedResources);
            Assert.Equal(1 + succeedCountFromProgress, result.SucceededResources);
        }

        private ImportProcessingJobDefinition GetInputData()
        {
            var inputData = new ImportProcessingJobDefinition();
            inputData.BaseUriString = "http://dummy";
            inputData.ResourceLocation = "http://dummy";
            inputData.ResourceType = "Patient";
            inputData.UriString = "http://dummy";

            return inputData;
        }

        private static JobInfo GetJobInfo(ImportProcessingJobDefinition data, ImportProcessingJobResult result)
        {
            var jobInfo = new JobInfo
            {
                Definition = JsonConvert.SerializeObject(data),
                Result = result != null ? JsonConvert.SerializeObject(result) : null,
            };

            return jobInfo;
        }
    }
}
