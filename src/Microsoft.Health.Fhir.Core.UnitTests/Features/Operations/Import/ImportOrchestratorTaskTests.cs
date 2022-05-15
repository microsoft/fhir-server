// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Core;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Operations.Import.Models;
using Microsoft.Health.TaskManagement;
using Microsoft.Health.TaskManagement.UnitTests;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Import
{
    public class ImportOrchestratorTaskTests
    {
        [Fact]
        public async Task GivenAnOrchestratorTask_WhenProcessingInputFilesMoreThanConcurrentCount_ThenTaskShouldBeCompleted()
        {
            await VerifyCommonOrchestratorTaskAsync(105, 6);
        }

        [Fact]
        public async Task GivenAnOrchestratorTask_WhenProcessingInputFilesEqualsConcurrentCount_ThenTaskShouldBeCompleted()
        {
            await VerifyCommonOrchestratorTaskAsync(105, 105);
        }

        [Fact]
        public async Task GivenAnOrchestratorTask_WhenProcessingInputFilesLessThanConcurrentCount_ThenTaskShouldBeCompleted()
        {
            await VerifyCommonOrchestratorTaskAsync(11, 105);
        }

        [Fact]
        public async Task GivenAnOrchestratorTask_WhenResumeFromFailure_ThenTaskShouldBeCompleted()
        {
            await VerifyCommonOrchestratorTaskAsync(105, 6, 10);
        }

        [Fact]
        public async Task GivenAnOrchestratorTask_WhenResumeFromFailureSomeTaskStillRunning_ThenTaskShouldBeCompleted()
        {
            await VerifyCommonOrchestratorTaskAsync(105, 6, 10, 5);
        }

        [Fact]
        public async Task GivenAnOrchestratorTaskAndWrongEtag_WhenOrchestratorTaskStart_ThenTaskShouldFailedWithDetails()
        {
            IImportOrchestratorTaskDataStoreOperation fhirDataBulkImportOperation = Substitute.For<IImportOrchestratorTaskDataStoreOperation>();
            RequestContextAccessor<IFhirRequestContext> contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            ILoggerFactory loggerFactory = new NullLoggerFactory();
            IIntegrationDataStoreClient integrationDataStoreClient = Substitute.For<IIntegrationDataStoreClient>();
            ImportOrchestratorTaskInputData importOrchestratorTaskInputData = new ImportOrchestratorTaskInputData();
            ImportOrchestratorTaskResult importOrchestratorTaskInputResult = new ImportOrchestratorTaskResult();

            IMediator mediator = Substitute.For<IMediator>();

            importOrchestratorTaskInputData.TaskCreateTime = Clock.UtcNow;
            importOrchestratorTaskInputData.BaseUri = new Uri("http://dummy");
            var inputs = new List<InputResource>();
            inputs.Add(new InputResource() { Type = "Resource", Url = new Uri("http://dummy"), Etag = "dummy" });
            importOrchestratorTaskInputData.Input = inputs;
            importOrchestratorTaskInputData.InputFormat = "ndjson";
            importOrchestratorTaskInputData.InputSource = new Uri("http://dummy");
            importOrchestratorTaskInputData.RequestUri = new Uri("http://dummy");

            integrationDataStoreClient.GetPropertiesAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    Dictionary<string, object> properties = new Dictionary<string, object>();
                    properties[IntegrationDataStoreClientConstants.BlobPropertyETag] = "test";
                    properties[IntegrationDataStoreClientConstants.BlobPropertyLength] = 1000L;
                    return properties;
                });
            TestQueueClient testQueueClient = new TestQueueClient();
            TaskInfo orchestratorTaskInfo = (await testQueueClient.EnqueueAsync(0, new string[] { JsonConvert.SerializeObject(importOrchestratorTaskInputData) }, 1, false, false, CancellationToken.None)).First();

            ImportOrchestratorTask orchestratorTask = new ImportOrchestratorTask(
                mediator,
                importOrchestratorTaskInputData,
                importOrchestratorTaskInputResult,
                contextAccessor,
                fhirDataBulkImportOperation,
                integrationDataStoreClient,
                testQueueClient,
                orchestratorTaskInfo,
                new Configs.ImportTaskConfiguration() { MaxRunningProcessingTaskCount = 1 },
                loggerFactory);

            TaskExecutionException taskExecutionException = await Assert.ThrowsAsync<TaskExecutionException>(async () => await orchestratorTask.ExecuteAsync(new Progress<string>(), CancellationToken.None));
            ImportOrchestratorTaskErrorResult resultDetails = (ImportOrchestratorTaskErrorResult)taskExecutionException.Error;

            Assert.Equal(HttpStatusCode.BadRequest, resultDetails.HttpStatusCode);
            Assert.NotEmpty(resultDetails.ErrorMessage);

            _ = mediator.Received().Publish(
                Arg.Is<ImportTaskMetricsNotification>(
                    notification => notification.Id == orchestratorTaskInfo.Id.ToString() &&
                    notification.Status == TaskResult.Fail.ToString() &&
                    notification.CreatedTime == importOrchestratorTaskInputData.TaskCreateTime &&
                    notification.DataSize == null &&
                    notification.SucceedCount == 0 &&
                    notification.FailedCount == 0),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAnOrchestratorTask_WhenIntegrationExceptionThrow_ThenTaskShouldFailedWithDetails()
        {
            IImportOrchestratorTaskDataStoreOperation fhirDataBulkImportOperation = Substitute.For<IImportOrchestratorTaskDataStoreOperation>();
            RequestContextAccessor<IFhirRequestContext> contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            ILoggerFactory loggerFactory = new NullLoggerFactory();
            IIntegrationDataStoreClient integrationDataStoreClient = Substitute.For<IIntegrationDataStoreClient>();
            ImportOrchestratorTaskInputData importOrchestratorTaskInputData = new ImportOrchestratorTaskInputData();
            IMediator mediator = Substitute.For<IMediator>();

            importOrchestratorTaskInputData.TaskCreateTime = Clock.UtcNow;
            importOrchestratorTaskInputData.BaseUri = new Uri("http://dummy");
            var inputs = new List<InputResource>();
            inputs.Add(new InputResource() { Type = "Resource", Url = new Uri("http://dummy"), Etag = "dummy" });
            importOrchestratorTaskInputData.Input = inputs;
            importOrchestratorTaskInputData.InputFormat = "ndjson";
            importOrchestratorTaskInputData.InputSource = new Uri("http://dummy");
            importOrchestratorTaskInputData.RequestUri = new Uri("http://dummy");

            integrationDataStoreClient.GetPropertiesAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
                .Returns<Task<Dictionary<string, object>>>(_ =>
                {
                    throw new IntegrationDataStoreException("dummy", HttpStatusCode.Unauthorized);
                });
            TestQueueClient testQueueClient = new TestQueueClient();
            TaskInfo orchestratorTaskInfo = (await testQueueClient.EnqueueAsync(0, new string[] { JsonConvert.SerializeObject(importOrchestratorTaskInputData) }, 1, false, false, CancellationToken.None)).First();

            ImportOrchestratorTask orchestratorTask = new ImportOrchestratorTask(
                mediator,
                importOrchestratorTaskInputData,
                new ImportOrchestratorTaskResult(),
                contextAccessor,
                fhirDataBulkImportOperation,
                integrationDataStoreClient,
                testQueueClient,
                orchestratorTaskInfo,
                new Configs.ImportTaskConfiguration() { MaxRunningProcessingTaskCount = 1},
                loggerFactory);

            TaskExecutionException taskExecutionException = await Assert.ThrowsAsync<TaskExecutionException>(async () => await orchestratorTask.ExecuteAsync(new Progress<string>(), CancellationToken.None));
            ImportOrchestratorTaskErrorResult resultDetails = (ImportOrchestratorTaskErrorResult)taskExecutionException.Error;

            Assert.Equal(HttpStatusCode.Unauthorized, resultDetails.HttpStatusCode);
            Assert.NotEmpty(resultDetails.ErrorMessage);

            _ = mediator.Received().Publish(
                Arg.Is<ImportTaskMetricsNotification>(
                    notification => notification.Id == orchestratorTaskInfo.Id.ToString() &&
                    notification.Status == TaskResult.Fail.ToString() &&
                    notification.CreatedTime == importOrchestratorTaskInputData.TaskCreateTime &&
                    notification.DataSize == null &&
                    notification.SucceedCount == 0 &&
                    notification.FailedCount == 0),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAnOrchestratorTask_WhenFailedAtPreprocessStep_ThenTaskExecutionExceptionShouldBeThrowAndContextUpdated()
        {
            IImportOrchestratorTaskDataStoreOperation fhirDataBulkImportOperation = Substitute.For<IImportOrchestratorTaskDataStoreOperation>();
            RequestContextAccessor<IFhirRequestContext> contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            ILoggerFactory loggerFactory = new NullLoggerFactory();
            IIntegrationDataStoreClient integrationDataStoreClient = Substitute.For<IIntegrationDataStoreClient>();
            IMediator mediator = Substitute.For<IMediator>();
            ImportOrchestratorTaskInputData importOrchestratorTaskInputData = new ImportOrchestratorTaskInputData();
            List<(long begin, long end)> surrogatedIdRanges = new List<(long begin, long end)>();

            importOrchestratorTaskInputData.TaskCreateTime = Clock.UtcNow;
            importOrchestratorTaskInputData.BaseUri = new Uri("http://dummy");
            var inputs = new List<InputResource>();
            inputs.Add(new InputResource() { Type = "Resource", Url = new Uri($"http://dummy") });

            importOrchestratorTaskInputData.Input = inputs;
            importOrchestratorTaskInputData.InputFormat = "ndjson";
            importOrchestratorTaskInputData.InputSource = new Uri("http://dummy");
            importOrchestratorTaskInputData.RequestUri = new Uri("http://dummy");

            integrationDataStoreClient.GetPropertiesAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    Dictionary<string, object> properties = new Dictionary<string, object>();
                    properties[IntegrationDataStoreClientConstants.BlobPropertyETag] = "test";
                    properties[IntegrationDataStoreClientConstants.BlobPropertyLength] = 1000L;
                    return properties;
                });

            fhirDataBulkImportOperation.PreprocessAsync(Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    throw new InvalidCastException();
                });

            TestQueueClient testQueueClient = new TestQueueClient();
            TaskInfo orchestratorTaskInfo = (await testQueueClient.EnqueueAsync(0, new string[] { JsonConvert.SerializeObject(importOrchestratorTaskInputData) }, 1, false, false, CancellationToken.None)).First();

            ImportOrchestratorTask orchestratorTask = new ImportOrchestratorTask(
                mediator,
                importOrchestratorTaskInputData,
                new ImportOrchestratorTaskResult(),
                contextAccessor,
                fhirDataBulkImportOperation,
                integrationDataStoreClient,
                testQueueClient,
                orchestratorTaskInfo,
                new Configs.ImportTaskConfiguration() { MaxRunningProcessingTaskCount = 1 },
                loggerFactory);
            orchestratorTask.PollingFrequencyInSeconds = 0;

            var taskExecutionException = await Assert.ThrowsAnyAsync<TaskExecutionException>(() => orchestratorTask.ExecuteAsync(new Progress<string>(), CancellationToken.None));
            ImportOrchestratorTaskErrorResult resultDetails = (ImportOrchestratorTaskErrorResult)taskExecutionException.Error;

            Assert.Equal(HttpStatusCode.InternalServerError, resultDetails.HttpStatusCode);
            Assert.NotEmpty(resultDetails.ErrorMessage);

            _ = mediator.Received().Publish(
                Arg.Is<ImportTaskMetricsNotification>(
                    notification => notification.Id == orchestratorTaskInfo.Id.ToString() &&
                    notification.Status == TaskResult.Fail.ToString() &&
                    notification.CreatedTime == importOrchestratorTaskInputData.TaskCreateTime &&
                    notification.DataSize == null &&
                    notification.SucceedCount == 0 &&
                    notification.FailedCount == 0),
                Arg.Any<CancellationToken>());
        }

        private static async Task VerifyCommonOrchestratorTaskAsync(int inputFileCount, int concurrentCount, int resumeFrom = -1, int completedCount = 0)
        {
            IImportOrchestratorTaskDataStoreOperation fhirDataBulkImportOperation = Substitute.For<IImportOrchestratorTaskDataStoreOperation>();
            RequestContextAccessor<IFhirRequestContext> contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            ILoggerFactory loggerFactory = new NullLoggerFactory();
            IIntegrationDataStoreClient integrationDataStoreClient = Substitute.For<IIntegrationDataStoreClient>();
            ISequenceIdGenerator<long> sequenceIdGenerator = Substitute.For<ISequenceIdGenerator<long>>();
            IMediator mediator = Substitute.For<IMediator>();
            ImportOrchestratorTaskInputData importOrchestratorTaskInputData = new ImportOrchestratorTaskInputData();
            ImportOrchestratorTaskResult importOrchestratorTaskResult = new ImportOrchestratorTaskResult();

            TestQueueClient testQueueClient = new TestQueueClient();
            List<(long begin, long end)> surrogatedIdRanges = new List<(long begin, long end)>();
            testQueueClient.GetTaskByIdFunc = (testQueueClient, id) =>
            {
                TaskInfo taskInfo = testQueueClient.TaskInfos.First(t => t.Id == id);

                if (taskInfo == null)
                {
                    return null;
                }

                if (taskInfo.Status == TaskManagement.TaskStatus.Completed)
                {
                    return taskInfo;
                }

                ImportProcessingTaskInputData processingInput = JsonConvert.DeserializeObject<ImportProcessingTaskInputData>(taskInfo.Definition);
                ImportProcessingTaskResult processingResult = new ImportProcessingTaskResult();
                processingResult.ResourceType = processingInput.ResourceType;
                processingResult.SucceedCount = 1;
                processingResult.FailedCount = 1;
                processingResult.ErrorLogLocation = "http://dummy/error";
                surrogatedIdRanges.Add((processingInput.BeginSequenceId, processingInput.EndSequenceId));

                taskInfo.Result = JsonConvert.SerializeObject(processingResult);
                taskInfo.Status = TaskManagement.TaskStatus.Completed;
                return taskInfo;
            };

            importOrchestratorTaskInputData.TaskCreateTime = Clock.UtcNow;
            importOrchestratorTaskInputData.BaseUri = new Uri("http://dummy");
            var inputs = new List<InputResource>();

            bool resumeMode = resumeFrom >= 0;
            for (int i = 0; i < inputFileCount; ++i)
            {
                string location = $"http://dummy/{i}";
                inputs.Add(new InputResource() { Type = "Resource", Url = new Uri(location) });

                if (resumeMode)
                {
                    if (i <= resumeFrom)
                    {
                        ImportProcessingTaskInputData processingInput = new ImportProcessingTaskInputData()
                        {
                            ResourceLocation = "http://test",
                            BeginSequenceId = i,
                            EndSequenceId = i + 1,
                        };

                        TaskInfo taskInfo = (await testQueueClient.EnqueueAsync(0, new string[] { JsonConvert.SerializeObject(processingInput) }, 1, false, false, CancellationToken.None)).First();

                        ImportProcessingTaskResult processingResult = new ImportProcessingTaskResult();
                        processingResult.ResourceType = "Resource";
                        processingResult.SucceedCount = 1;
                        processingResult.FailedCount = 1;
                        processingResult.ErrorLogLocation = "http://dummy/error";
                        processingResult.ResourceLocation = location;

                        taskInfo.Result = JsonConvert.SerializeObject(processingResult);
                        if (i < completedCount)
                        {
                            taskInfo.Status = TaskManagement.TaskStatus.Completed;
                            importOrchestratorTaskResult.SucceedImportCount += 1;
                            importOrchestratorTaskResult.FailedImportCount += 1;
                        }
                        else
                        {
                            taskInfo.Status = TaskManagement.TaskStatus.Running;
                            importOrchestratorTaskResult.RunningTaskIds.Add(taskInfo.Id);
                        }

                        importOrchestratorTaskResult.CreatedTaskCount += 1;
                        importOrchestratorTaskResult.CurrentSequenceId += 1;
                    }

                    importOrchestratorTaskResult.Progress = ImportOrchestratorTaskProgress.PreprocessCompleted;
                }
            }

            importOrchestratorTaskInputData.Input = inputs;
            importOrchestratorTaskInputData.InputFormat = "ndjson";
            importOrchestratorTaskInputData.InputSource = new Uri("http://dummy");
            importOrchestratorTaskInputData.RequestUri = new Uri("http://dummy");
            TaskInfo orchestratorTaskInfo = (await testQueueClient.EnqueueAsync(0, new string[] { JsonConvert.SerializeObject(importOrchestratorTaskInputData) }, 1, false, false, CancellationToken.None)).First();

            integrationDataStoreClient.GetPropertiesAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    Dictionary<string, object> properties = new Dictionary<string, object>();
                    properties[IntegrationDataStoreClientConstants.BlobPropertyETag] = "test";
                    properties[IntegrationDataStoreClientConstants.BlobPropertyLength] = 1000L;
                    return properties;
                });

            sequenceIdGenerator.GetCurrentSequenceId().Returns(_ => 0L);

            ImportOrchestratorTask orchestratorTask = new ImportOrchestratorTask(
                mediator,
                importOrchestratorTaskInputData,
                importOrchestratorTaskResult,
                contextAccessor,
                fhirDataBulkImportOperation,
                integrationDataStoreClient,
                testQueueClient,
                orchestratorTaskInfo,
                new Configs.ImportTaskConfiguration() { MaxRunningProcessingTaskCount = concurrentCount },
                loggerFactory);
            orchestratorTask.PollingFrequencyInSeconds = 0;

            string result = await orchestratorTask.ExecuteAsync(new Progress<string>(), CancellationToken.None);
            ImportOrchestratorTaskResult resultDetails = JsonConvert.DeserializeObject<ImportOrchestratorTaskResult>(result);
            Assert.NotEmpty(resultDetails.Request);
            Assert.Equal(importOrchestratorTaskInputData.TaskCreateTime, resultDetails.TransactionTime);

            Assert.Equal(inputFileCount, testQueueClient.TaskInfos.Count() - 1);

            var orderedSurrogatedIdRanges = surrogatedIdRanges.OrderBy(r => r.begin).ToArray();
            Assert.Equal(inputFileCount, orderedSurrogatedIdRanges.Length + completedCount);
            for (int i = 0; i < orderedSurrogatedIdRanges.Length - 1; ++i)
            {
                Assert.True(orderedSurrogatedIdRanges[i].end > orderedSurrogatedIdRanges[i].begin);
                Assert.True(orderedSurrogatedIdRanges[i].end <= orderedSurrogatedIdRanges[i + 1].begin);
            }

            _ = mediator.Received().Publish(
                Arg.Is<ImportTaskMetricsNotification>(
                    notification => notification.Id.Equals(orchestratorTaskInfo.Id.ToString()) &&
                    notification.Status == TaskResult.Success.ToString() &&
                    notification.CreatedTime == importOrchestratorTaskInputData.TaskCreateTime &&
                    notification.SucceedCount == inputFileCount &&
                    notification.FailedCount == inputFileCount),
                Arg.Any<CancellationToken>());
        }
    }
}
