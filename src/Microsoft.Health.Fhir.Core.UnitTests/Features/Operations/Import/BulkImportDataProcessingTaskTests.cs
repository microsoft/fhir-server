// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.TaskManagement;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Import
{
    public class BulkImportDataProcessingTaskTests
    {
        private const string ResourceTableName = "ResourceTableName";

        [Fact]
        public async Task GivenValidInputFiles_WhenExecuteImportTask_ResourcesShouldBeImported()
        {
            int count = 101;
            List<string> inputData = new List<string>();
            List<BulkImportResourceWrapper> result = new List<BulkImportResourceWrapper>();

            for (int i = 0; i < count; ++i)
            {
                inputData.Add(i.ToString());
            }

            BulkImportDataProcessingInputData inputDataPayload = new BulkImportDataProcessingInputData();
            inputDataPayload.StartSurrogateId = 10;
            inputDataPayload.EndSurrogateId = 200;
            inputDataPayload.ResourceType = "Test";
            inputDataPayload.ResourceLocation = "http://dummy";
            inputDataPayload.UriString = "http://dummy";
            inputDataPayload.BaseUriString = "http://dummy";
            inputDataPayload.TaskId = Guid.NewGuid().ToString("N");

            BulkImportProgress progress = new BulkImportProgress();
            IFhirDataBulkOperation batchOperation = GetMockFhirDataBulkOperation(null);
            IContextUpdater contextUpdater = GetMockContextUpdater(null);
            IBulkResourceLoader bulkResourceLoader = GetMockBulkResourceLoader(inputData);
            IImportErrorUploader uploader = GetMockImportErrorUploader(null);
            IBulkRawResourceProcessor processor = GetMockResourceProcessor();
            IBulkImporter<BulkImportResourceWrapper> importer = GetMockBulkImporter(7, resource => result.Add(resource));
            IFhirRequestContextAccessor accessor = new FhirRequestContextAccessor();
            ILoggerFactory loggerFactory = new NullLoggerFactory();

            BulkImportDataProcessingTask testTask = new BulkImportDataProcessingTask(inputDataPayload, progress, batchOperation, contextUpdater, bulkResourceLoader, uploader, processor, importer, accessor, loggerFactory);
            TaskResultData taskResult = await testTask.ExecuteAsync();
            BulkImportDataProcessingTaskResult resultData = JsonConvert.DeserializeObject<BulkImportDataProcessingTaskResult>(taskResult.ResultData);

            Assert.Equal(TaskResult.Success, taskResult.Result);
            Assert.Equal(count, result.Count);
            Assert.Equal(inputDataPayload.ResourceType, resultData.ResourceType);
            Assert.Equal(count, resultData.CompletedResourceCount);
            Assert.Equal(0, resultData.FailedResourceCount);
        }

        [Fact]
        public async Task GivenValidInputFiles_WhenRecoveryFromCrash_ResourcesShouldBeImported()
        {
            int count = 101;
            List<string> inputData = new List<string>();
            List<BulkImportResourceWrapper> result = new List<BulkImportResourceWrapper>();

            for (int i = 0; i < count; ++i)
            {
                inputData.Add(i.ToString());
            }

            BulkImportDataProcessingInputData inputDataPayload = new BulkImportDataProcessingInputData();
            inputDataPayload.StartSurrogateId = 10;
            inputDataPayload.EndSurrogateId = 200;
            inputDataPayload.ResourceType = "Test";
            inputDataPayload.ResourceLocation = "http://dummy";
            inputDataPayload.UriString = "http://dummy";
            inputDataPayload.BaseUriString = "http://dummy";
            inputDataPayload.TaskId = Guid.NewGuid().ToString("N");

            BulkImportProgress progress = new BulkImportProgress();
            progress.ProgressRecords[ResourceTableName] = new ProgressRecord(20);

            IFhirDataBulkOperation batchOperation = GetMockFhirDataBulkOperation(null);
            IContextUpdater contextUpdater = GetMockContextUpdater(null);
            IBulkResourceLoader bulkResourceLoader = GetMockBulkResourceLoader(inputData);
            IImportErrorUploader uploader = GetMockImportErrorUploader(null);
            IBulkRawResourceProcessor processor = GetMockResourceProcessor();
            IBulkImporter<BulkImportResourceWrapper> importer = GetMockBulkImporter(7, resource => result.Add(resource));
            IFhirRequestContextAccessor accessor = new FhirRequestContextAccessor();
            ILoggerFactory loggerFactory = new NullLoggerFactory();

            BulkImportDataProcessingTask testTask = new BulkImportDataProcessingTask(inputDataPayload, progress, batchOperation, contextUpdater, bulkResourceLoader, uploader, processor, importer, accessor, loggerFactory);
            TaskResultData taskResult = await testTask.ExecuteAsync();
            BulkImportDataProcessingTaskResult resultData = JsonConvert.DeserializeObject<BulkImportDataProcessingTaskResult>(taskResult.ResultData);

            Assert.Equal(TaskResult.Success, taskResult.Result);
            Assert.Equal(count, result.Count - 10);
            Assert.Equal(inputDataPayload.ResourceType, resultData.ResourceType);
            Assert.Equal(0, resultData.FailedResourceCount);
        }

        private IFhirDataBulkOperation GetMockFhirDataBulkOperation(Action<long, long> cleanBatchResourceAction)
        {
            IFhirDataBulkOperation fhirDataBulkOperation = Substitute.For<IFhirDataBulkOperation>();
            fhirDataBulkOperation.CleanBatchResourceAsync(Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
                .ReturnsForAnyArgs(callInfo =>
                {
                    long startSurrogateId = (long)callInfo[0];
                    long endSurrogateId = (long)callInfo[1];

                    cleanBatchResourceAction?.Invoke(startSurrogateId, endSurrogateId);
                    return Task.CompletedTask;
                });

            return fhirDataBulkOperation;
        }

        private IContextUpdater GetMockContextUpdater(Action<string> updateContextAction)
        {
            IContextUpdater contextUpdater = Substitute.For<IContextUpdater>();
            contextUpdater.UpdateContextAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .ReturnsForAnyArgs(callInfo =>
                {
                    string context = (string)callInfo[0];

                    updateContextAction?.Invoke(context);
                    return Task.CompletedTask;
                });

            return contextUpdater;
        }

        private IBulkResourceLoader GetMockBulkResourceLoader(List<string> data)
        {
            IBulkResourceLoader bulkResourceLoader = Substitute.For<IBulkResourceLoader>();
            bulkResourceLoader.LoadToChannelAsync(Arg.Any<Channel<string>>(), Arg.Any<Uri>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
                .ReturnsForAnyArgs(async callInfo =>
                {
                    Channel<string> rawDataChannel = (Channel<string>)callInfo[0];
                    long startLineOffset = (long)callInfo[2];

                    for (int i = 0; i < data.Count; ++i)
                    {
                        if (i < startLineOffset)
                        {
                            continue;
                        }

                        await rawDataChannel.Writer.WriteAsync(data[i]);
                    }

                    rawDataChannel.Writer.Complete();
                });

            return bulkResourceLoader;
        }

        private IImportErrorUploader GetMockImportErrorUploader(Action<BatchProcessErrorRecord> errorHandleAction)
        {
            IImportErrorUploader importErrorUploader = Substitute.For<IImportErrorUploader>();
            importErrorUploader.HandleImportErrorAsync(Arg.Any<string>(), Arg.Any<Channel<BatchProcessErrorRecord>>(), Arg.Any<long>(), Arg.Any<Action<long, long>>(), Arg.Any<CancellationToken>())
                .ReturnsForAnyArgs(async callInfo =>
                {
                    long result = 0;
                    Channel<BatchProcessErrorRecord> errorChannel = (Channel<BatchProcessErrorRecord>)callInfo[1];
                    do
                    {
                        await foreach (BatchProcessErrorRecord errorRecord in errorChannel.Reader.ReadAllAsync())
                        {
                            result += errorRecord.ProcessErrors.Count();
                            errorHandleAction?.Invoke(errorRecord);
                        }
                    }
                    while (await errorChannel.Reader.WaitToReadAsync());

                    return (result, new Uri("http://dummy"));
                });

            return importErrorUploader;
        }

        private IBulkRawResourceProcessor GetMockResourceProcessor()
        {
            IBulkImportDataExtractor bulkImportDataExtractor = Substitute.For<IBulkImportDataExtractor>();
            bulkImportDataExtractor.GetBulkImportResourceWrapper(Arg.Any<string>())
                .Returns(callInfo =>
                {
                    string content = (string)callInfo[0];

                    if (string.IsNullOrEmpty(content))
                    {
                        throw new InvalidOperationException("ErrorMessage");
                    }

                    return new BulkImportResourceWrapper(null, Encoding.UTF8.GetBytes(content));
                });

            BulkRawResourceProcessor processor = new BulkRawResourceProcessor(bulkImportDataExtractor, NullLogger<BulkRawResourceProcessor>.Instance);
            return processor;
        }

        private IBulkImporter<BulkImportResourceWrapper> GetMockBulkImporter(int batchSize, Action<BulkImportResourceWrapper> importAction)
        {
            IBulkImporter<BulkImportResourceWrapper> bulkImporter = Substitute.For<IBulkImporter<BulkImportResourceWrapper>>();
            bulkImporter.ImportResourceAsync(Arg.Any<Channel<BulkImportResourceWrapper>>(), Arg.Any<Action<(string tableName, long endSurrogateId)>>(), Arg.Any<CancellationToken>())
                .Returns(async callInfo =>
                {
                    Channel<BulkImportResourceWrapper> inputChannel = (Channel<BulkImportResourceWrapper>)callInfo[0];
                    Action<(string tableName, long endSurrogateId)> progress = (Action<(string tableName, long endSurrogateId)>)callInfo[1];
                    long result = 0;
                    long surrogatedId = 0;

                    do
                    {
                        await foreach (BulkImportResourceWrapper resource in inputChannel.Reader.ReadAllAsync())
                        {
                            importAction?.Invoke(resource);
                            result++;
                            surrogatedId = resource.ResourceSurrogateId;

                            if (result % batchSize == 0)
                            {
                                progress((ResourceTableName, surrogatedId));
                            }
                        }
                    }
                    while (await inputChannel.Reader.WaitToReadAsync());

                    progress((ResourceTableName, surrogatedId));

                    return result;
                });

            return bulkImporter;
        }
    }
}
