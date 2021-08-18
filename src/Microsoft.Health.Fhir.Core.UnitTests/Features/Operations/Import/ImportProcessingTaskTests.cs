// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.TaskManagement;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Import
{
    public class ImportProcessingTaskTests
    {
        [Fact]
        public async Task GivenImportTaskInput_WhenStartFromClean_ThenAllResoruceShouldBeImported()
        {
            ImportProcessingTaskInputData inputData = GetInputData();
            ImportProcessingProgress progress = new ImportProcessingProgress();
            await VerifyCommonImportTaskAsync(inputData, progress);
        }

        [Fact]
        public async Task GivenImportTaskInput_WhenStartFromMiddle_ThenAllResoruceShouldBeImported()
        {
            ImportProcessingTaskInputData inputData = GetInputData();
            ImportProcessingProgress progress = new ImportProcessingProgress();
            progress.SucceedImportCount = 3;
            progress.FailedImportCount = 1;
            progress.CurrentIndex = 4;

            await VerifyCommonImportTaskAsync(inputData, progress);
        }

        [Fact]
        public async Task GivenImportTaskInput_WhenExceptionThrowForImport_ThenContextShouldBeUpdatedBeforeFailure()
        {
            long currentIndex = 100;
            ImportProcessingTaskInputData inputData = GetInputData();
            ImportProcessingProgress progress = new ImportProcessingProgress();

            IImportResourceLoader loader = Substitute.For<IImportResourceLoader>();
            IResourceBulkImporter importer = Substitute.For<IResourceBulkImporter>();
            IImportErrorStore importErrorStore = Substitute.For<IImportErrorStore>();
            IImportErrorStoreFactory importErrorStoreFactory = Substitute.For<IImportErrorStoreFactory>();
            IContextUpdater contextUpdater = Substitute.For<IContextUpdater>();
            RequestContextAccessor<IFhirRequestContext> contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            ILoggerFactory loggerFactory = new NullLoggerFactory();

            loader.LoadResources(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<Func<long, long>>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    long startIndex = (long)callInfo[1];
                    Func<long, long> idGenerator = (Func<long, long>)callInfo[3];
                    Channel<ImportResource> resourceChannel = Channel.CreateUnbounded<ImportResource>();
                    resourceChannel.Writer.Complete();

                    return (resourceChannel, Task.CompletedTask);
                });

            importer.Import(Arg.Any<Channel<ImportResource>>(), Arg.Any<IImportErrorStore>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    Channel<ImportProcessingProgress> progressChannel = Channel.CreateUnbounded<ImportProcessingProgress>();

                    Task loadTask = Task.Run(async () =>
                    {
                        try
                        {
                            ImportProcessingProgress progress = new ImportProcessingProgress();
                            progress.CurrentIndex = currentIndex;

                            await progressChannel.Writer.WriteAsync(progress);
                            throw new InvalidOperationException();
                        }
                        finally
                        {
                            progressChannel.Writer.Complete();
                        }
                    });

                    return (progressChannel, loadTask);
                });

            string context = null;
            contextUpdater.UpdateContextAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    context = (string)callInfo[0];

                    return Task.CompletedTask;
                });

            ImportProcessingTask task = new ImportProcessingTask(
                                    inputData,
                                    progress,
                                    loader,
                                    importer,
                                    importErrorStoreFactory,
                                    contextUpdater,
                                    contextAccessor,
                                    loggerFactory);

            await Assert.ThrowsAsync<RetriableTaskException>(() => task.ExecuteAsync());

            ImportProcessingProgress progressForContext = JsonConvert.DeserializeObject<ImportProcessingProgress>(context);
            Assert.Equal(progressForContext.CurrentIndex, currentIndex);
        }

        [Fact]
        public async Task GivenImportTaskInput_WhenExceptionThrowForLoad_ThenRetriableExceptionShouldBeThrow()
        {
            ImportProcessingTaskInputData inputData = GetInputData();
            ImportProcessingProgress progress = new ImportProcessingProgress();

            IImportResourceLoader loader = Substitute.For<IImportResourceLoader>();
            IResourceBulkImporter importer = Substitute.For<IResourceBulkImporter>();
            IImportErrorStore importErrorStore = Substitute.For<IImportErrorStore>();
            IImportErrorStoreFactory importErrorStoreFactory = Substitute.For<IImportErrorStoreFactory>();
            IContextUpdater contextUpdater = Substitute.For<IContextUpdater>();
            RequestContextAccessor<IFhirRequestContext> contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            ILoggerFactory loggerFactory = new NullLoggerFactory();

            loader.LoadResources(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<Func<long, long>>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    long startIndex = (long)callInfo[1];
                    Func<long, long> idGenerator = (Func<long, long>)callInfo[3];
                    Channel<ImportResource> resourceChannel = Channel.CreateUnbounded<ImportResource>();

                    Task loadTask = Task.Run(() =>
                    {
                        try
                        {
                            throw new InvalidOperationException();
                        }
                        finally
                        {
                            resourceChannel.Writer.Complete();
                        }
                    });

                    return (resourceChannel, loadTask);
                });

            ImportProcessingTask task = new ImportProcessingTask(
                                    inputData,
                                    progress,
                                    loader,
                                    importer,
                                    importErrorStoreFactory,
                                    contextUpdater,
                                    contextAccessor,
                                    loggerFactory);

            await Assert.ThrowsAsync<RetriableTaskException>(() => task.ExecuteAsync());
        }

        [Fact]
        public async Task GivenImportTaskInput_WhenExceptionThrowForCleanData_ThenRetriableExceptionShouldBeThrow()
        {
            ImportProcessingTaskInputData inputData = GetInputData();
            ImportProcessingProgress progress = new ImportProcessingProgress();

            IImportResourceLoader loader = Substitute.For<IImportResourceLoader>();
            IResourceBulkImporter importer = Substitute.For<IResourceBulkImporter>();
            IImportErrorStore importErrorStore = Substitute.For<IImportErrorStore>();
            IImportErrorStoreFactory importErrorStoreFactory = Substitute.For<IImportErrorStoreFactory>();
            IContextUpdater contextUpdater = Substitute.For<IContextUpdater>();
            RequestContextAccessor<IFhirRequestContext> contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            ILoggerFactory loggerFactory = new NullLoggerFactory();

            importer.CleanResourceAsync(Arg.Any<ImportProcessingTaskInputData>(), Arg.Any<ImportProcessingProgress>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    throw new InvalidOperationException();
                });

            progress.NeedCleanData = true;
            ImportProcessingTask task = new ImportProcessingTask(
                                    inputData,
                                    progress,
                                    loader,
                                    importer,
                                    importErrorStoreFactory,
                                    contextUpdater,
                                    contextAccessor,
                                    loggerFactory);

            await Assert.ThrowsAsync<RetriableTaskException>(() => task.ExecuteAsync());
        }

        [Fact]
        public async Task GivenImportTaskInput_WhenOperationWasCancelledExceptionThrow_ThenTaskShouldBeCanceled()
        {
            ImportProcessingTaskInputData inputData = GetInputData();
            ImportProcessingProgress progress = new ImportProcessingProgress();

            IImportResourceLoader loader = Substitute.For<IImportResourceLoader>();
            IResourceBulkImporter importer = Substitute.For<IResourceBulkImporter>();
            IImportErrorStore importErrorStore = Substitute.For<IImportErrorStore>();
            IImportErrorStoreFactory importErrorStoreFactory = Substitute.For<IImportErrorStoreFactory>();
            IContextUpdater contextUpdater = Substitute.For<IContextUpdater>();
            RequestContextAccessor<IFhirRequestContext> contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            ILoggerFactory loggerFactory = new NullLoggerFactory();

            importer.Import(Arg.Any<Channel<ImportResource>>(), Arg.Any<IImportErrorStore>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    throw new OperationCanceledException();
                });

            ImportProcessingTask task = new ImportProcessingTask(
                                    inputData,
                                    progress,
                                    loader,
                                    importer,
                                    importErrorStoreFactory,
                                    contextUpdater,
                                    contextAccessor,
                                    loggerFactory);

            TaskResultData result = await task.ExecuteAsync();
            Assert.Equal(TaskResult.Canceled, result.Result);
        }

        private static async Task VerifyCommonImportTaskAsync(ImportProcessingTaskInputData inputData, ImportProcessingProgress progress)
        {
            long startIndexFromProgress = progress.CurrentIndex;
            long succeedCountFromProgress = progress.SucceedImportCount;
            long failedCountFromProgress = progress.FailedImportCount;

            IImportResourceLoader loader = Substitute.For<IImportResourceLoader>();
            IResourceBulkImporter importer = Substitute.For<IResourceBulkImporter>();
            IImportErrorStore importErrorStore = Substitute.For<IImportErrorStore>();
            IImportErrorStoreFactory importErrorStoreFactory = Substitute.For<IImportErrorStoreFactory>();
            IContextUpdater contextUpdater = Substitute.For<IContextUpdater>();
            RequestContextAccessor<IFhirRequestContext> contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            ILoggerFactory loggerFactory = new NullLoggerFactory();

            long cleanStart = -1;
            long cleanEnd = -1;
            importer.CleanResourceAsync(Arg.Any<ImportProcessingTaskInputData>(), Arg.Any<ImportProcessingProgress>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    var inputData = (ImportProcessingTaskInputData)callInfo[0];
                    var progress = (ImportProcessingProgress)callInfo[1];
                    long beginSequenceId = inputData.BeginSequenceId;
                    long endSequenceId = inputData.EndSequenceId;
                    long endIndex = progress.CurrentIndex;

                    cleanStart = beginSequenceId + endIndex;
                    cleanEnd = endSequenceId;

                    return Task.CompletedTask;
                });

            loader.LoadResources(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<string>(), Arg.Any<Func<long, long>>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    long startIndex = (long)callInfo[1];
                    Func<long, long> idGenerator = (Func<long, long>)callInfo[3];
                    Channel<ImportResource> resourceChannel = Channel.CreateUnbounded<ImportResource>();

                    Task loadTask = Task.Run(async () =>
                    {
                        ResourceWrapper resourceWrapper = new ResourceWrapper(
                            Guid.NewGuid().ToString(),
                            "0",
                            "Dummy",
                            new RawResource(Guid.NewGuid().ToString(), Fhir.Core.Models.FhirResourceFormat.Json, true),
                            new ResourceRequest("POST"),
                            DateTimeOffset.UtcNow,
                            false,
                            null,
                            null,
                            null,
                            "SearchParam");

                        await resourceChannel.Writer.WriteAsync(new ImportResource(idGenerator(startIndex), startIndex, resourceWrapper));
                        await resourceChannel.Writer.WriteAsync(new ImportResource(idGenerator(startIndex + 1), startIndex + 1, "Error"));
                        resourceChannel.Writer.Complete();
                    });

                    return (resourceChannel, loadTask);
                });

            importer.Import(Arg.Any<Channel<ImportResource>>(), Arg.Any<IImportErrorStore>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    Channel<ImportResource> resourceChannel = (Channel<ImportResource>)callInfo[0];
                    Channel<ImportProcessingProgress> progressChannel = Channel.CreateUnbounded<ImportProcessingProgress>();

                    Task loadTask = Task.Run(async () =>
                    {
                        ImportProcessingProgress progress = new ImportProcessingProgress();
                        await foreach (ImportResource resource in resourceChannel.Reader.ReadAllAsync())
                        {
                            if (string.IsNullOrEmpty(resource.ImportError))
                            {
                                progress.SucceedImportCount++;
                            }
                            else
                            {
                                progress.FailedImportCount++;
                            }

                            progress.CurrentIndex = resource.Index + 1;
                        }

                        await progressChannel.Writer.WriteAsync(progress);
                        progressChannel.Writer.Complete();
                    });

                    return (progressChannel, loadTask);
                });

            string context = null;
            contextUpdater.UpdateContextAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    context = (string)callInfo[0];

                    return Task.CompletedTask;
                });

            progress.NeedCleanData = true;
            ImportProcessingTask task = new ImportProcessingTask(
                                    inputData,
                                    progress,
                                    loader,
                                    importer,
                                    importErrorStoreFactory,
                                    contextUpdater,
                                    contextAccessor,
                                    loggerFactory);

            TaskResultData taskResult = await task.ExecuteAsync();
            Assert.Equal(TaskResult.Success, taskResult.Result);
            ImportProcessingTaskResult result = JsonConvert.DeserializeObject<ImportProcessingTaskResult>(taskResult.ResultData);
            Assert.Equal(1 + failedCountFromProgress, result.FailedCount);
            Assert.Equal(1 + succeedCountFromProgress, result.SucceedCount);

            ImportProcessingProgress progressForContext = JsonConvert.DeserializeObject<ImportProcessingProgress>(context);
            Assert.Equal(progressForContext.SucceedImportCount, result.SucceedCount);
            Assert.Equal(progressForContext.FailedImportCount, result.FailedCount);
            Assert.Equal(startIndexFromProgress + 2, progressForContext.CurrentIndex);

            Assert.Equal(startIndexFromProgress, cleanStart);
            Assert.Equal(inputData.EndSequenceId, cleanEnd);
        }

        private ImportProcessingTaskInputData GetInputData()
        {
            ImportProcessingTaskInputData inputData = new ImportProcessingTaskInputData();
            inputData.BaseUriString = "http://dummy";
            inputData.ResourceLocation = "http://dummy";
            inputData.ResourceType = "Resource";
            inputData.TaskId = Guid.NewGuid().ToString("N");
            inputData.UriString = "http://dummy";

            return inputData;
        }
    }
}
