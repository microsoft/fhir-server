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
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.TaskManagement;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Import
{
    public class ImportTaskTests
    {
        [Fact]
        public async Task GivenImportTaskInput_WhenCleanStart_AllResoruceShouldBeImported()
        {
            ImportTaskInputData inputData = GetInputData();
            ImportProgress progress = new ImportProgress();
            await VerifyCommonImportTaskAsync(inputData, progress);
        }

        [Fact]
        public async Task GivenImportTaskInput_WhenStartFromMiddle_AllResoruceShouldBeImported()
        {
            ImportTaskInputData inputData = GetInputData();
            ImportProgress progress = new ImportProgress();
            progress.SucceedImportCount = 3;
            progress.FailedImportCount = 1;
            progress.CurrentIndex = 4;

            await VerifyCommonImportTaskAsync(inputData, progress);
        }

        [Fact]
        public async Task GivenImportTaskInput_WhenExceptionThrowForImport_ContextShouldBeUpdatedBeforeFailure()
        {
            long currentIndex = 100;
            ImportTaskInputData inputData = GetInputData();
            ImportProgress progress = new ImportProgress();

            IFhirDataBulkImportOperation bulkOperation = Substitute.For<IFhirDataBulkImportOperation>();
            IImportResourceLoader loader = Substitute.For<IImportResourceLoader>();
            IResourceBulkImporter importer = Substitute.For<IResourceBulkImporter>();
            IImportErrorStore importErrorStore = Substitute.For<IImportErrorStore>();
            IImportErrorStoreFactory importErrorStoreFactory = Substitute.For<IImportErrorStoreFactory>();
            IContextUpdater contextUpdater = Substitute.For<IContextUpdater>();
            RequestContextAccessor<IFhirRequestContext> contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            ILoggerFactory loggerFactory = new NullLoggerFactory();

            loader.LoadResources(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<Func<long, long>>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    long startIndex = (long)callInfo[1];
                    Func<long, long> idGenerator = (Func<long, long>)callInfo[2];
                    Channel<ImportResource> resourceChannel = Channel.CreateUnbounded<ImportResource>();
                    resourceChannel.Writer.Complete();

                    return (resourceChannel, Task.CompletedTask);
                });

            importer.Import(Arg.Any<Channel<ImportResource>>(), Arg.Any<IImportErrorStore>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    Channel<ImportProgress> progressChannel = Channel.CreateUnbounded<ImportProgress>();

                    Task loadTask = Task.Run(async () =>
                    {
                        try
                        {
                            ImportProgress progress = new ImportProgress();
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

            ImportTask task = new ImportTask(
                                    inputData,
                                    progress,
                                    bulkOperation,
                                    loader,
                                    importer,
                                    importErrorStoreFactory,
                                    contextUpdater,
                                    contextAccessor,
                                    loggerFactory);

            await Assert.ThrowsAsync<RetriableTaskException>(() => task.ExecuteAsync());

            ImportProgress progressForContext = JsonConvert.DeserializeObject<ImportProgress>(context);
            Assert.Equal(progressForContext.CurrentIndex, currentIndex);
        }

        [Fact]
        public async Task GivenImportTaskInput_WhenExceptionThrowForLoad_RetriableExceptionShouldBeThrow()
        {
            ImportTaskInputData inputData = GetInputData();
            ImportProgress progress = new ImportProgress();

            IFhirDataBulkImportOperation bulkOperation = Substitute.For<IFhirDataBulkImportOperation>();
            IImportResourceLoader loader = Substitute.For<IImportResourceLoader>();
            IResourceBulkImporter importer = Substitute.For<IResourceBulkImporter>();
            IImportErrorStore importErrorStore = Substitute.For<IImportErrorStore>();
            IImportErrorStoreFactory importErrorStoreFactory = Substitute.For<IImportErrorStoreFactory>();
            IContextUpdater contextUpdater = Substitute.For<IContextUpdater>();
            RequestContextAccessor<IFhirRequestContext> contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            ILoggerFactory loggerFactory = new NullLoggerFactory();

            loader.LoadResources(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<Func<long, long>>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    long startIndex = (long)callInfo[1];
                    Func<long, long> idGenerator = (Func<long, long>)callInfo[2];
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

            importer.Import(Arg.Any<Channel<ImportResource>>(), Arg.Any<IImportErrorStore>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    Channel<ImportProgress> progressChannel = Channel.CreateUnbounded<ImportProgress>();

                    Task loadTask = Task.Run(async () =>
                    {
                        try
                        {
                            ImportProgress progress = new ImportProgress();

                            await progressChannel.Writer.WriteAsync(progress);
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

            ImportTask task = new ImportTask(
                                    inputData,
                                    progress,
                                    bulkOperation,
                                    loader,
                                    importer,
                                    importErrorStoreFactory,
                                    contextUpdater,
                                    contextAccessor,
                                    loggerFactory);

            await Assert.ThrowsAsync<RetriableTaskException>(() => task.ExecuteAsync());
        }

        [Fact]
        public async Task GivenImportTaskInput_WhenExceptionThrowForCleanData_RetriableExceptionShouldBeThrow()
        {
            ImportTaskInputData inputData = GetInputData();
            ImportProgress progress = new ImportProgress();

            IFhirDataBulkImportOperation bulkOperation = Substitute.For<IFhirDataBulkImportOperation>();
            IImportResourceLoader loader = Substitute.For<IImportResourceLoader>();
            IResourceBulkImporter importer = Substitute.For<IResourceBulkImporter>();
            IImportErrorStore importErrorStore = Substitute.For<IImportErrorStore>();
            IImportErrorStoreFactory importErrorStoreFactory = Substitute.For<IImportErrorStoreFactory>();
            IContextUpdater contextUpdater = Substitute.For<IContextUpdater>();
            RequestContextAccessor<IFhirRequestContext> contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            ILoggerFactory loggerFactory = new NullLoggerFactory();

            bulkOperation.CleanBatchResourceAsync(Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    throw new InvalidOperationException();
                });

            ImportTask task = new ImportTask(
                                    inputData,
                                    progress,
                                    bulkOperation,
                                    loader,
                                    importer,
                                    importErrorStoreFactory,
                                    contextUpdater,
                                    contextAccessor,
                                    loggerFactory);

            await Assert.ThrowsAsync<RetriableTaskException>(() => task.ExecuteAsync());
        }

        [Fact]
        public async Task GivenImportTaskInput_WhenOperationWasCancelledExceptionThrow_TaskShouldBeCanceled()
        {
            ImportTaskInputData inputData = GetInputData();
            ImportProgress progress = new ImportProgress();

            IFhirDataBulkImportOperation bulkOperation = Substitute.For<IFhirDataBulkImportOperation>();
            IImportResourceLoader loader = Substitute.For<IImportResourceLoader>();
            IResourceBulkImporter importer = Substitute.For<IResourceBulkImporter>();
            IImportErrorStore importErrorStore = Substitute.For<IImportErrorStore>();
            IImportErrorStoreFactory importErrorStoreFactory = Substitute.For<IImportErrorStoreFactory>();
            IContextUpdater contextUpdater = Substitute.For<IContextUpdater>();
            RequestContextAccessor<IFhirRequestContext> contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            ILoggerFactory loggerFactory = new NullLoggerFactory();

            bulkOperation.CleanBatchResourceAsync(Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    throw new OperationCanceledException();
                });

            ImportTask task = new ImportTask(
                                    inputData,
                                    progress,
                                    bulkOperation,
                                    loader,
                                    importer,
                                    importErrorStoreFactory,
                                    contextUpdater,
                                    contextAccessor,
                                    loggerFactory);

            TaskResultData result = await task.ExecuteAsync();
            Assert.Equal(TaskResult.Canceled, result.Result);
        }

        private static async Task VerifyCommonImportTaskAsync(ImportTaskInputData inputData, ImportProgress progress)
        {
            long startIndexFromProgress = progress.CurrentIndex;
            long succeedCountFromProgress = progress.SucceedImportCount;
            long failedCountFromProgress = progress.FailedImportCount;

            IFhirDataBulkImportOperation bulkOperation = Substitute.For<IFhirDataBulkImportOperation>();
            IImportResourceLoader loader = Substitute.For<IImportResourceLoader>();
            IResourceBulkImporter importer = Substitute.For<IResourceBulkImporter>();
            IImportErrorStore importErrorStore = Substitute.For<IImportErrorStore>();
            IImportErrorStoreFactory importErrorStoreFactory = Substitute.For<IImportErrorStoreFactory>();
            IContextUpdater contextUpdater = Substitute.For<IContextUpdater>();
            RequestContextAccessor<IFhirRequestContext> contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            ILoggerFactory loggerFactory = new NullLoggerFactory();

            long cleanStart = -1;
            long cleanEnd = -1;
            bulkOperation.CleanBatchResourceAsync(Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    cleanStart = (long)callInfo[0];
                    cleanEnd = (long)callInfo[1];

                    return Task.CompletedTask;
                });

            loader.LoadResources(Arg.Any<string>(), Arg.Any<long>(), Arg.Any<Func<long, long>>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    long startIndex = (long)callInfo[1];
                    Func<long, long> idGenerator = (Func<long, long>)callInfo[2];
                    Channel<ImportResource> resourceChannel = Channel.CreateUnbounded<ImportResource>();

                    Task loadTask = Task.Run(async () =>
                    {
                        await resourceChannel.Writer.WriteAsync(new ImportResource(idGenerator(startIndex), startIndex, null, null));
                        await resourceChannel.Writer.WriteAsync(new ImportResource(idGenerator(startIndex + 1), startIndex + 1, "Error"));
                        resourceChannel.Writer.Complete();
                    });

                    return (resourceChannel, loadTask);
                });

            importer.Import(Arg.Any<Channel<ImportResource>>(), Arg.Any<IImportErrorStore>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    Channel<ImportResource> resourceChannel = (Channel<ImportResource>)callInfo[0];
                    Channel<ImportProgress> progressChannel = Channel.CreateUnbounded<ImportProgress>();

                    Task loadTask = Task.Run(async () =>
                    {
                        ImportProgress progress = new ImportProgress();
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

            ImportTask task = new ImportTask(
                                    inputData,
                                    progress,
                                    bulkOperation,
                                    loader,
                                    importer,
                                    importErrorStoreFactory,
                                    contextUpdater,
                                    contextAccessor,
                                    loggerFactory);

            TaskResultData taskResult = await task.ExecuteAsync();
            Assert.Equal(TaskResult.Success, taskResult.Result);
            ImportTaskResult result = JsonConvert.DeserializeObject<ImportTaskResult>(taskResult.ResultData);
            Assert.Equal(1 + failedCountFromProgress, result.FailedCount);
            Assert.Equal(1 + succeedCountFromProgress, result.SucceedCount);

            ImportProgress progressForContext = JsonConvert.DeserializeObject<ImportProgress>(context);
            Assert.Equal(progressForContext.SucceedImportCount, result.SucceedCount);
            Assert.Equal(progressForContext.FailedImportCount, result.FailedCount);
            Assert.Equal(startIndexFromProgress + 2, progressForContext.CurrentIndex);

            Assert.Equal(startIndexFromProgress, cleanStart);
            Assert.Equal(inputData.EndSequenceId, cleanEnd);
        }

        private ImportTaskInputData GetInputData()
        {
            ImportTaskInputData inputData = new ImportTaskInputData();
            inputData.BaseUriString = "http://dummy";
            inputData.ResourceLocation = "http://dummy";
            inputData.ResourceType = "Resource";
            inputData.TaskId = Guid.NewGuid().ToString("N");
            inputData.UriString = "http://dummy";

            return inputData;
        }
    }
}
