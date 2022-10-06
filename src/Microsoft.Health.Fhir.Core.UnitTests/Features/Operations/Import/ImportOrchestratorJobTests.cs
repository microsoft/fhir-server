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
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.JobManagement.UnitTests;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Import
{
    [Trait(Traits.OwningTeam, OwningTeam.FhirImport)]
    [Trait(Traits.Category, Categories.Import)]
    public class ImportOrchestratorJobTests
    {
        [Fact]
        public async Task GivenAnOrchestratorJob_WhenProcessingInputFilesMoreThanConcurrentCount_ThenJobShouldBeCompleted()
        {
            await VerifyCommonOrchestratorJobAsync(105, 6);
        }

        [Fact]
        public async Task GivenAnOrchestratorJob_WhenProcessingInputFilesEqualsConcurrentCount_ThenJobShouldBeCompleted()
        {
            await VerifyCommonOrchestratorJobAsync(105, 105);
        }

        [Fact]
        public async Task GivenAnOrchestratorJob_WhenProcessingInputFilesLessThanConcurrentCount_ThenJobShouldBeCompleted()
        {
            await VerifyCommonOrchestratorJobAsync(11, 105);
        }

        [Fact]
        public async Task GivenAnOrchestratorJob_WhenResumeFromFailure_ThenJobShouldBeCompleted()
        {
            await VerifyCommonOrchestratorJobAsync(105, 6, 10);
        }

        [Fact]
        public async Task GivenAnOrchestratorJob_WhenAllResumeFromFailure_ThenJobShouldBeCompleted()
        {
            await VerifyCommonOrchestratorJobAsync(105, 6, 105);
        }

        [Fact]
        public async Task GivenAnOrchestratorJob_WhenResumeFromFailureSomeJobStillRunning_ThenJobShouldBeCompleted()
        {
            await VerifyCommonOrchestratorJobAsync(105, 6, 10, 5);
        }

        [Fact]
        public async Task GivenAnOrchestratorJob_WhenSomeJobsCancelled_ThenOperationCanceledExceptionShouldBeThrowAndWaitForOtherSubJobsCompleted()
        {
            await VerifyJobStatusChangedAsync(100, 1, JobStatus.Cancelled, 20, 20);
        }

        [Fact]
        public async Task GivenAnOrchestratorJob_WhenSomeJobsFailed_ThenImportProcessingExceptionShouldBeThrowAndWaitForOtherSubJobsCompleted()
        {
            await VerifyJobStatusChangedAsync(100, 1, JobStatus.Failed, 14, 14);
        }

        [Fact]
        public async Task GivenAnOrchestratorJobAndWrongEtag_WhenOrchestratorJobStart_ThenJobShouldFailedWithDetails()
        {
            IImportOrchestratorJobDataStoreOperation fhirDataBulkImportOperation = Substitute.For<IImportOrchestratorJobDataStoreOperation>();
            RequestContextAccessor<IFhirRequestContext> contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            ILoggerFactory loggerFactory = new NullLoggerFactory();
            IIntegrationDataStoreClient integrationDataStoreClient = Substitute.For<IIntegrationDataStoreClient>();
            ImportOrchestratorJobInputData importOrchestratorInputData = new ImportOrchestratorJobInputData();
            ImportOrchestratorJobResult importOrchestratorInputResult = new ImportOrchestratorJobResult();

            IMediator mediator = Substitute.For<IMediator>();

            importOrchestratorInputData.CreateTime = Clock.UtcNow;
            importOrchestratorInputData.BaseUri = new Uri("http://dummy");
            var inputs = new List<InputResource>();
            inputs.Add(new InputResource() { Type = "Resource", Url = new Uri("http://dummy"), Etag = "dummy" });
            importOrchestratorInputData.Input = inputs;
            importOrchestratorInputData.InputFormat = "ndjson";
            importOrchestratorInputData.InputSource = new Uri("http://dummy");
            importOrchestratorInputData.RequestUri = new Uri("http://dummy");

            integrationDataStoreClient.GetPropertiesAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    Dictionary<string, object> properties = new Dictionary<string, object>();
                    properties[IntegrationDataStoreClientConstants.BlobPropertyETag] = "test";
                    properties[IntegrationDataStoreClientConstants.BlobPropertyLength] = 1000L;
                    return properties;
                });
            TestQueueClient testQueueClient = new TestQueueClient();
            JobInfo orchestratorJobInfo = (await testQueueClient.EnqueueAsync(0, new string[] { JsonConvert.SerializeObject(importOrchestratorInputData) }, 1, false, false, CancellationToken.None)).First();

            ImportOrchestratorJob orchestratorJob = new ImportOrchestratorJob(
                mediator,
                importOrchestratorInputData,
                importOrchestratorInputResult,
                contextAccessor,
                fhirDataBulkImportOperation,
                integrationDataStoreClient,
                testQueueClient,
                orchestratorJobInfo,
                new Configs.ImportTaskConfiguration() { MaxRunningProcessingJobCount = 1 },
                loggerFactory);

            JobExecutionException jobExecutionException = await Assert.ThrowsAsync<JobExecutionException>(async () => await orchestratorJob.ExecuteAsync(new Progress<string>(), CancellationToken.None));
            ImportOrchestratorJobErrorResult resultDetails = (ImportOrchestratorJobErrorResult)jobExecutionException.Error;

            Assert.Equal(HttpStatusCode.BadRequest, resultDetails.HttpStatusCode);
            Assert.NotEmpty(resultDetails.ErrorMessage);

            _ = mediator.Received().Publish(
                Arg.Is<ImportJobMetricsNotification>(
                    notification => notification.Id == orchestratorJobInfo.Id.ToString() &&
                    notification.Status == JobStatus.Failed.ToString() &&
                    notification.CreatedTime == importOrchestratorInputData.CreateTime &&
                    notification.DataSize == null &&
                    notification.SucceedCount == 0 &&
                    notification.FailedCount == 0),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAnOrchestratorJob_WhenIntegrationExceptionThrow_ThenJobShouldFailedWithDetails()
        {
            IImportOrchestratorJobDataStoreOperation fhirDataBulkImportOperation = Substitute.For<IImportOrchestratorJobDataStoreOperation>();
            RequestContextAccessor<IFhirRequestContext> contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            ILoggerFactory loggerFactory = new NullLoggerFactory();
            IIntegrationDataStoreClient integrationDataStoreClient = Substitute.For<IIntegrationDataStoreClient>();
            ImportOrchestratorJobInputData importOrchestratorJobInputData = new ImportOrchestratorJobInputData();
            IMediator mediator = Substitute.For<IMediator>();

            importOrchestratorJobInputData.CreateTime = Clock.UtcNow;
            importOrchestratorJobInputData.BaseUri = new Uri("http://dummy");
            var inputs = new List<InputResource>();
            inputs.Add(new InputResource() { Type = "Resource", Url = new Uri("http://dummy"), Etag = "dummy" });
            importOrchestratorJobInputData.Input = inputs;
            importOrchestratorJobInputData.InputFormat = "ndjson";
            importOrchestratorJobInputData.InputSource = new Uri("http://dummy");
            importOrchestratorJobInputData.RequestUri = new Uri("http://dummy");

            integrationDataStoreClient.GetPropertiesAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
                .Returns<Task<Dictionary<string, object>>>(_ =>
                {
                    throw new IntegrationDataStoreException("dummy", HttpStatusCode.Unauthorized);
                });
            TestQueueClient testQueueClient = new TestQueueClient();
            JobInfo orchestratorJobInfo = (await testQueueClient.EnqueueAsync(0, new string[] { JsonConvert.SerializeObject(importOrchestratorJobInputData) }, 1, false, false, CancellationToken.None)).First();

            ImportOrchestratorJob orchestratorJob = new ImportOrchestratorJob(
                mediator,
                importOrchestratorJobInputData,
                new ImportOrchestratorJobResult(),
                contextAccessor,
                fhirDataBulkImportOperation,
                integrationDataStoreClient,
                testQueueClient,
                orchestratorJobInfo,
                new Configs.ImportTaskConfiguration() { MaxRunningProcessingJobCount = 1},
                loggerFactory);

            JobExecutionException jobExecutionException = await Assert.ThrowsAsync<JobExecutionException>(async () => await orchestratorJob.ExecuteAsync(new Progress<string>(), CancellationToken.None));
            ImportOrchestratorJobErrorResult resultDetails = (ImportOrchestratorJobErrorResult)jobExecutionException.Error;

            Assert.Equal(HttpStatusCode.Unauthorized, resultDetails.HttpStatusCode);
            Assert.NotEmpty(resultDetails.ErrorMessage);

            _ = mediator.Received().Publish(
                Arg.Is<ImportJobMetricsNotification>(
                    notification => notification.Id == orchestratorJobInfo.Id.ToString() &&
                    notification.Status == JobStatus.Failed.ToString() &&
                    notification.CreatedTime == importOrchestratorJobInputData.CreateTime &&
                    notification.DataSize == null &&
                    notification.SucceedCount == 0 &&
                    notification.FailedCount == 0),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAnOrchestratorJob_WhenFailedAtPreprocessStep_ThenJobExecutionExceptionShouldBeThrowAndContextUpdated()
        {
            IImportOrchestratorJobDataStoreOperation fhirDataBulkImportOperation = Substitute.For<IImportOrchestratorJobDataStoreOperation>();
            RequestContextAccessor<IFhirRequestContext> contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            ILoggerFactory loggerFactory = new NullLoggerFactory();
            IIntegrationDataStoreClient integrationDataStoreClient = Substitute.For<IIntegrationDataStoreClient>();
            IMediator mediator = Substitute.For<IMediator>();
            ImportOrchestratorJobInputData importOrchestratorJobInputData = new ImportOrchestratorJobInputData();
            List<(long begin, long end)> surrogatedIdRanges = new List<(long begin, long end)>();

            importOrchestratorJobInputData.CreateTime = Clock.UtcNow;
            importOrchestratorJobInputData.BaseUri = new Uri("http://dummy");
            var inputs = new List<InputResource>();
            inputs.Add(new InputResource() { Type = "Resource", Url = new Uri($"http://dummy") });

            importOrchestratorJobInputData.Input = inputs;
            importOrchestratorJobInputData.InputFormat = "ndjson";
            importOrchestratorJobInputData.InputSource = new Uri("http://dummy");
            importOrchestratorJobInputData.RequestUri = new Uri("http://dummy");

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
            JobInfo orchestratorJobInfo = (await testQueueClient.EnqueueAsync(0, new string[] { JsonConvert.SerializeObject(importOrchestratorJobInputData) }, 1, false, false, CancellationToken.None)).First();

            ImportOrchestratorJob orchestratorJob = new ImportOrchestratorJob(
                mediator,
                importOrchestratorJobInputData,
                new ImportOrchestratorJobResult(),
                contextAccessor,
                fhirDataBulkImportOperation,
                integrationDataStoreClient,
                testQueueClient,
                orchestratorJobInfo,
                new Configs.ImportTaskConfiguration() { MaxRunningProcessingJobCount = 1 },
                loggerFactory);
            orchestratorJob.PollingFrequencyInSeconds = 0;

            var jobExecutionException = await Assert.ThrowsAnyAsync<JobExecutionException>(() => orchestratorJob.ExecuteAsync(new Progress<string>(), CancellationToken.None));
            ImportOrchestratorJobErrorResult resultDetails = (ImportOrchestratorJobErrorResult)jobExecutionException.Error;

            Assert.Equal(HttpStatusCode.InternalServerError, resultDetails.HttpStatusCode);
            Assert.NotEmpty(resultDetails.ErrorMessage);

            _ = mediator.Received().Publish(
                Arg.Is<ImportJobMetricsNotification>(
                    notification => notification.Id == orchestratorJobInfo.Id.ToString() &&
                    notification.Status == JobStatus.Failed.ToString() &&
                    notification.CreatedTime == importOrchestratorJobInputData.CreateTime &&
                    notification.DataSize == null &&
                    notification.SucceedCount == 0 &&
                    notification.FailedCount == 0),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAnOrchestratorJob_WhenRetriableExceptionThrow_ThenJobExecutionShuldFailedWithRetriableException()
        {
            IImportOrchestratorJobDataStoreOperation fhirDataBulkImportOperation = Substitute.For<IImportOrchestratorJobDataStoreOperation>();
            RequestContextAccessor<IFhirRequestContext> contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            ILoggerFactory loggerFactory = new NullLoggerFactory();
            IIntegrationDataStoreClient integrationDataStoreClient = Substitute.For<IIntegrationDataStoreClient>();
            IMediator mediator = Substitute.For<IMediator>();
            ImportOrchestratorJobInputData importOrchestratorInputData = new ImportOrchestratorJobInputData();
            List<(long begin, long end)> surrogatedIdRanges = new List<(long begin, long end)>();

            importOrchestratorInputData.CreateTime = Clock.UtcNow;
            importOrchestratorInputData.BaseUri = new Uri("http://dummy");
            var inputs = new List<InputResource>();
            inputs.Add(new InputResource() { Type = "Resource", Url = new Uri($"http://dummy") });

            importOrchestratorInputData.Input = inputs;
            importOrchestratorInputData.InputFormat = "ndjson";
            importOrchestratorInputData.InputSource = new Uri("http://dummy");
            importOrchestratorInputData.RequestUri = new Uri("http://dummy");

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
                    throw new RetriableJobException("test");
                });

            TestQueueClient testQueueClient = new TestQueueClient();
            JobInfo orchestratorJobInfo = (await testQueueClient.EnqueueAsync(0, new string[] { JsonConvert.SerializeObject(importOrchestratorInputData) }, 1, false, false, CancellationToken.None)).First();

            ImportOrchestratorJob orchestratorJob = new ImportOrchestratorJob(
                mediator,
                importOrchestratorInputData,
                new ImportOrchestratorJobResult(),
                contextAccessor,
                fhirDataBulkImportOperation,
                integrationDataStoreClient,
                testQueueClient,
                orchestratorJobInfo,
                new Configs.ImportTaskConfiguration() { MaxRunningProcessingJobCount = 1 },
                loggerFactory);
            orchestratorJob.PollingFrequencyInSeconds = 0;

            await Assert.ThrowsAnyAsync<RetriableJobException>(() => orchestratorJob.ExecuteAsync(new Progress<string>(), CancellationToken.None));

            _ = mediator.DidNotReceive().Publish(
                Arg.Any<ImportJobMetricsNotification>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAnOrchestratorJob_WhenLastSubJobFailed_ThenImportProcessingExceptionShouldBeThrowAndWaitForOtherSubJobsCancelledAndCompleted()
        {
            IImportOrchestratorJobDataStoreOperation fhirDataBulkImportOperation = Substitute.For<IImportOrchestratorJobDataStoreOperation>();
            RequestContextAccessor<IFhirRequestContext> contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            ILoggerFactory loggerFactory = new NullLoggerFactory();
            IIntegrationDataStoreClient integrationDataStoreClient = Substitute.For<IIntegrationDataStoreClient>();
            ISequenceIdGenerator<long> sequenceIdGenerator = Substitute.For<ISequenceIdGenerator<long>>();
            IMediator mediator = Substitute.For<IMediator>();
            ImportOrchestratorJobInputData importOrchestratorInputData = new ImportOrchestratorJobInputData();
            ImportOrchestratorJobResult importOrchestratorJobResult = new ImportOrchestratorJobResult();
            TestQueueClient testQueueClient = new TestQueueClient();
            bool getJobByGroupIdCalledTime = false;

            testQueueClient.GetJobByIdFunc = (queueClient, id, _) =>
            {
                JobInfo jobInfo = queueClient.JobInfos.First(t => t.Id == id);
                if (jobInfo.Id == 3)
                {
                    jobInfo.Status = JobStatus.Failed;
                    jobInfo.Result = JsonConvert.SerializeObject(new ImportProcessingJobErrorResult() { Message = "Job Failed" });
                }

                return jobInfo;
            };
            testQueueClient.GetJobByGroupIdFunc = (queueClient, groupId, _) =>
            {
                IEnumerable<JobInfo> jobInfos = queueClient.JobInfos.Where(t => t.GroupId == groupId);
                if (!getJobByGroupIdCalledTime)
                {
                    foreach (JobInfo jobInfo in jobInfos)
                    {
                        if (jobInfo.Status == JobStatus.Running)
                        {
                            jobInfo.Status = JobStatus.Completed;
                        }
                    }
                }

                getJobByGroupIdCalledTime = true;
                return jobInfos;
            };
            importOrchestratorInputData.CreateTime = Clock.UtcNow;
            importOrchestratorInputData.BaseUri = new Uri("http://dummy");
            var inputs = new List<InputResource>();

            // completed 2, running 1, created 2.
            for (int i = 0; i < 3; i++)
            {
                string location = $"http://dummy/{i}";
                inputs.Add(new InputResource() { Type = "Resource", Url = new Uri(location) });
                ImportProcessingJobInputData processingInput = new ImportProcessingJobInputData()
                {
                    ResourceLocation = "http://test",
                    BeginSequenceId = i,
                    EndSequenceId = i + 1,
                };
                JobInfo jobInfo = (await testQueueClient.EnqueueAsync(0, new string[] { JsonConvert.SerializeObject(processingInput) }, 1, false, false, CancellationToken.None)).First();
                ImportProcessingJobResult processingResult = new ImportProcessingJobResult();
                processingResult.ResourceType = "Resource";
                processingResult.SucceedCount = 1;
                processingResult.FailedCount = 1;
                processingResult.ErrorLogLocation = "http://dummy/error";
                processingResult.ResourceLocation = location;
                jobInfo.Result = JsonConvert.SerializeObject(processingResult);

                if (i == 0)
                {
                    jobInfo.Status = JobStatus.Completed;
                    importOrchestratorJobResult.SucceedImportCount += 1;
                    importOrchestratorJobResult.FailedImportCount += 1;
                }
                else
                {
                    jobInfo.Status = JobStatus.Running;
                    importOrchestratorJobResult.RunningJobIds.Add(jobInfo.Id);
                }

                importOrchestratorJobResult.CreatedJobCount += 1;
            }

            importOrchestratorJobResult.Progress = ImportOrchestratorJobProgress.PreprocessCompleted;
            inputs.Add(new InputResource() { Type = "Resource", Url = new Uri($"http://dummy/3") });
            inputs.Add(new InputResource() { Type = "Resource", Url = new Uri($"http://dummy/4") });
            importOrchestratorInputData.Input = inputs;
            importOrchestratorInputData.InputFormat = "ndjson";
            importOrchestratorInputData.InputSource = new Uri("http://dummy");
            importOrchestratorInputData.RequestUri = new Uri("http://dummy");
            JobInfo orchestratorJobInfo = (await testQueueClient.EnqueueAsync(0, new string[] { JsonConvert.SerializeObject(importOrchestratorInputData) }, 1, false, false, CancellationToken.None)).First();

            integrationDataStoreClient.GetPropertiesAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    Dictionary<string, object> properties = new Dictionary<string, object>();
                    properties[IntegrationDataStoreClientConstants.BlobPropertyETag] = "test";
                    properties[IntegrationDataStoreClientConstants.BlobPropertyLength] = 1000L;
                    return properties;
                });

            sequenceIdGenerator.GetCurrentSequenceId().Returns(_ => 0L);

            ImportOrchestratorJob orchestratorJob = new ImportOrchestratorJob(
                mediator,
                importOrchestratorInputData,
                importOrchestratorJobResult,
                contextAccessor,
                fhirDataBulkImportOperation,
                integrationDataStoreClient,
                testQueueClient,
                orchestratorJobInfo,
                new Configs.ImportTaskConfiguration() { MaxRunningProcessingJobCount = 3 },
                loggerFactory);
            orchestratorJob.PollingFrequencyInSeconds = 0;
            var jobExecutionException = await Assert.ThrowsAnyAsync<JobExecutionException>(() => orchestratorJob.ExecuteAsync(new Progress<string>(), CancellationToken.None));

            ImportOrchestratorJobErrorResult resultDetails = (ImportOrchestratorJobErrorResult)jobExecutionException.Error;
            Assert.Equal(HttpStatusCode.BadRequest, resultDetails.HttpStatusCode);
            Assert.Equal(2, testQueueClient.JobInfos.Count(t => t.Status == JobStatus.Completed));
            Assert.Equal(1, testQueueClient.JobInfos.Count(t => t.Status == JobStatus.Failed));
            Assert.Equal(2, testQueueClient.JobInfos.Count(t => t.Status == JobStatus.Cancelled));

            _ = mediator.Received().Publish(
               Arg.Is<ImportJobMetricsNotification>(
                   notification => notification.Id == orchestratorJobInfo.Id.ToString() &&
                   notification.Status == JobStatus.Failed.ToString() &&
                   notification.CreatedTime == importOrchestratorInputData.CreateTime &&
                   notification.SucceedCount == 1 &&
                   notification.FailedCount == 1),
               Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAnOrchestratorJob_WhenSubJobFailedAndOthersRunning_ThenImportProcessingExceptionShouldBeThrowAndContextUpdated()
        {
            IImportOrchestratorJobDataStoreOperation fhirDataBulkImportOperation = Substitute.For<IImportOrchestratorJobDataStoreOperation>();
            RequestContextAccessor<IFhirRequestContext> contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            ILoggerFactory loggerFactory = new NullLoggerFactory();
            IIntegrationDataStoreClient integrationDataStoreClient = Substitute.For<IIntegrationDataStoreClient>();
            ISequenceIdGenerator<long> sequenceIdGenerator = Substitute.For<ISequenceIdGenerator<long>>();
            IMediator mediator = Substitute.For<IMediator>();
            ImportOrchestratorJobInputData importOrchestratorInputData = new ImportOrchestratorJobInputData();
            TestQueueClient testQueueClient = new TestQueueClient();
            bool getJobByGroupIdCalledTime = false;
            testQueueClient.GetJobByIdFunc = (queueClient, id, _) =>
            {
                if (id > 10)
                {
                    return new JobInfo()
                    {
                        Id = id,
                        Status = JobManagement.JobStatus.Failed,
                        Result = JsonConvert.SerializeObject(new ImportProcessingJobErrorResult() { Message = "error" }),
                    };
                }

                JobInfo jobInfo = testQueueClient.JobInfos.First(t => t.Id == id);
                if (jobInfo.Status == JobStatus.Created)
                {
                    jobInfo.Status = JobStatus.Running;
                    return jobInfo;
                }

                return jobInfo;
            };
            testQueueClient.GetJobByGroupIdFunc = (queueClient, groupId, _) =>
            {
                IEnumerable<JobInfo> jobInfos = queueClient.JobInfos.Where(t => t.GroupId == groupId);
                if (!getJobByGroupIdCalledTime)
                {
                    foreach (JobInfo jobInfo in jobInfos)
                    {
                        if (jobInfo.Status == JobStatus.Running)
                        {
                            jobInfo.Status = JobStatus.Completed;
                        }
                    }
                }

                getJobByGroupIdCalledTime = true;
                return jobInfos;
            };

            importOrchestratorInputData.CreateTime = Clock.UtcNow;
            importOrchestratorInputData.BaseUri = new Uri("http://dummy");

            var inputs = new List<InputResource>();
            for (int i = 0; i < 100; i++)
            {
                inputs.Add(new InputResource() { Type = "Resource", Url = new Uri($"http://dummy/{i}") });
            }

            importOrchestratorInputData.Input = inputs;
            importOrchestratorInputData.InputFormat = "ndjson";
            importOrchestratorInputData.InputSource = new Uri("http://dummy");
            importOrchestratorInputData.RequestUri = new Uri("http://dummy");
            JobInfo orchestratorJobInfo = (await testQueueClient.EnqueueAsync(0, new string[] { JsonConvert.SerializeObject(importOrchestratorInputData) }, 1, false, false, CancellationToken.None)).First();

            integrationDataStoreClient.GetPropertiesAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    Dictionary<string, object> properties = new Dictionary<string, object>();
                    properties[IntegrationDataStoreClientConstants.BlobPropertyETag] = "test";
                    properties[IntegrationDataStoreClientConstants.BlobPropertyLength] = 1000L;
                    return properties;
                });

            sequenceIdGenerator.GetCurrentSequenceId().Returns<long>(_ => 0L);
            ImportOrchestratorJob orchestratorJob = new ImportOrchestratorJob(
                mediator,
                importOrchestratorInputData,
                new ImportOrchestratorJobResult(),
                contextAccessor,
                fhirDataBulkImportOperation,
                integrationDataStoreClient,
                testQueueClient,
                orchestratorJobInfo,
                new Configs.ImportTaskConfiguration() { MaxRunningProcessingJobCount = 30 },
                loggerFactory);
            orchestratorJob.PollingFrequencyInSeconds = 0;

            var jobExecutionException = await Assert.ThrowsAnyAsync<JobExecutionException>(() => orchestratorJob.ExecuteAsync(new Progress<string>(), CancellationToken.None));
            ImportOrchestratorJobErrorResult resultDetails = (ImportOrchestratorJobErrorResult)jobExecutionException.Error;

            Assert.Equal(HttpStatusCode.BadRequest, resultDetails.HttpStatusCode);

            _ = mediator.Received().Publish(
                Arg.Is<ImportJobMetricsNotification>(
                    notification => notification.Id == orchestratorJobInfo.Id.ToString() &&
                    notification.Status == JobStatus.Failed.ToString() &&
                    notification.CreatedTime == importOrchestratorInputData.CreateTime &&
                    notification.SucceedCount == 0 &&
                    notification.FailedCount == 0),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAnOrchestratorJob_WhneSubJobCancelledAfterThreeCalls_ThenOperationCanceledExceptionShouldBeThrowAndContextUpdate()
        {
            IImportOrchestratorJobDataStoreOperation fhirDataBulkImportOperation = Substitute.For<IImportOrchestratorJobDataStoreOperation>();
            RequestContextAccessor<IFhirRequestContext> contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            ILoggerFactory loggerFactory = new NullLoggerFactory();
            IIntegrationDataStoreClient integrationDataStoreClient = Substitute.For<IIntegrationDataStoreClient>();
            ISequenceIdGenerator<long> sequenceIdGenerator = Substitute.For<ISequenceIdGenerator<long>>();
            IMediator mediator = Substitute.For<IMediator>();
            ImportOrchestratorJobInputData importOrchestratorJobInputData = new ImportOrchestratorJobInputData();
            TestQueueClient testQueueClient = new TestQueueClient();
            int callTime = 0;
            testQueueClient.GetJobByIdFunc = (queueClient, id, _) =>
            {
                JobInfo jobInfo = queueClient.JobInfos.First(t => t.Id == id);
                if (++callTime > 3)
                {
                    jobInfo.Status = JobStatus.Cancelled;
                    jobInfo.Result = JsonConvert.SerializeObject(new ImportProcessingJobErrorResult() { Message = "Job Cancelled" });
                }

                return jobInfo;
            };
            importOrchestratorJobInputData.CreateTime = Clock.UtcNow;
            importOrchestratorJobInputData.BaseUri = new Uri("http://dummy");

            var inputs = new List<InputResource>();
            inputs.Add(new InputResource() { Type = "Resource", Url = new Uri($"http://dummy") });

            importOrchestratorJobInputData.Input = inputs;
            importOrchestratorJobInputData.InputFormat = "ndjson";
            importOrchestratorJobInputData.InputSource = new Uri("http://dummy");
            importOrchestratorJobInputData.RequestUri = new Uri("http://dummy");

            integrationDataStoreClient.GetPropertiesAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    Dictionary<string, object> properties = new Dictionary<string, object>();
                    properties[IntegrationDataStoreClientConstants.BlobPropertyETag] = "test";
                    properties[IntegrationDataStoreClientConstants.BlobPropertyLength] = 1000L;
                    return properties;
                });

            sequenceIdGenerator.GetCurrentSequenceId().Returns<long>(_ => 0L);
            JobInfo orchestratorJobInfo = (await testQueueClient.EnqueueAsync(0, new string[] { JsonConvert.SerializeObject(importOrchestratorJobInputData) }, 1, false, false, CancellationToken.None)).First();
            ImportOrchestratorJob orchestratorJob = new ImportOrchestratorJob(
                mediator,
                importOrchestratorJobInputData,
                new ImportOrchestratorJobResult(),
                contextAccessor,
                fhirDataBulkImportOperation,
                integrationDataStoreClient,
                testQueueClient,
                orchestratorJobInfo,
                new Configs.ImportTaskConfiguration() { MaxRunningProcessingJobCount = 1 },
                loggerFactory);
            orchestratorJob.PollingFrequencyInSeconds = 0;

            var jobExecutionException = await Assert.ThrowsAnyAsync<JobExecutionException>(() => orchestratorJob.ExecuteAsync(new Progress<string>(), CancellationToken.None));
            ImportOrchestratorJobErrorResult resultDetails = (ImportOrchestratorJobErrorResult)jobExecutionException.Error;

            Assert.Equal(HttpStatusCode.BadRequest, resultDetails.HttpStatusCode);

            _ = mediator.Received().Publish(
                Arg.Is<ImportJobMetricsNotification>(
                    notification => notification.Id == orchestratorJobInfo.Id.ToString() &&
                    notification.Status == JobStatus.Cancelled.ToString() &&
                    notification.CreatedTime == importOrchestratorJobInputData.CreateTime &&
                    notification.SucceedCount == 0 &&
                    notification.FailedCount == 0),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAnOrchestratorJob_WhenSubJobFailedAfterThreeCalls_ThenImportProcessingExceptionShouldBeThrowAndContextUpdated()
        {
            IImportOrchestratorJobDataStoreOperation fhirDataBulkImportOperation = Substitute.For<IImportOrchestratorJobDataStoreOperation>();
            RequestContextAccessor<IFhirRequestContext> contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            ILoggerFactory loggerFactory = new NullLoggerFactory();
            IIntegrationDataStoreClient integrationDataStoreClient = Substitute.For<IIntegrationDataStoreClient>();
            ISequenceIdGenerator<long> sequenceIdGenerator = Substitute.For<ISequenceIdGenerator<long>>();
            IMediator mediator = Substitute.For<IMediator>();
            ImportOrchestratorJobInputData importOrchestratorJobInputData = new ImportOrchestratorJobInputData();
            TestQueueClient testQueueClient = new TestQueueClient();
            int callTime = 0;
            testQueueClient.GetJobByIdFunc = (queueClient, id, _) =>
            {
                JobInfo jobInfo = queueClient.JobInfos.First(t => t.Id == id);
                if (++callTime > 3)
                {
                    jobInfo.Status = JobStatus.Failed;
                    jobInfo.Result = JsonConvert.SerializeObject(new ImportProcessingJobErrorResult() { Message = "error" });
                }

                return jobInfo;
            };
            importOrchestratorJobInputData.CreateTime = Clock.UtcNow;
            importOrchestratorJobInputData.BaseUri = new Uri("http://dummy");

            var inputs = new List<InputResource>();
            inputs.Add(new InputResource() { Type = "Resource", Url = new Uri($"http://dummy") });

            importOrchestratorJobInputData.Input = inputs;
            importOrchestratorJobInputData.InputFormat = "ndjson";
            importOrchestratorJobInputData.InputSource = new Uri("http://dummy");
            importOrchestratorJobInputData.RequestUri = new Uri("http://dummy");

            integrationDataStoreClient.GetPropertiesAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    Dictionary<string, object> properties = new Dictionary<string, object>();
                    properties[IntegrationDataStoreClientConstants.BlobPropertyETag] = "test";
                    properties[IntegrationDataStoreClientConstants.BlobPropertyLength] = 1000L;
                    return properties;
                });

            sequenceIdGenerator.GetCurrentSequenceId().Returns<long>(_ => 0L);
            JobInfo orchestratorJobInfo = (await testQueueClient.EnqueueAsync(0, new string[] { JsonConvert.SerializeObject(importOrchestratorJobInputData) }, 1, false, false, CancellationToken.None)).First();
            ImportOrchestratorJob orchestratorJob = new ImportOrchestratorJob(
                mediator,
                importOrchestratorJobInputData,
                new ImportOrchestratorJobResult(),
                contextAccessor,
                fhirDataBulkImportOperation,
                integrationDataStoreClient,
                testQueueClient,
                orchestratorJobInfo,
                new Configs.ImportTaskConfiguration() { MaxRunningProcessingJobCount = 1 },
                loggerFactory);
            orchestratorJob.PollingFrequencyInSeconds = 0;

            var jobExecutionException = await Assert.ThrowsAnyAsync<JobExecutionException>(() => orchestratorJob.ExecuteAsync(new Progress<string>(), CancellationToken.None));
            ImportOrchestratorJobErrorResult resultDetails = (ImportOrchestratorJobErrorResult)jobExecutionException.Error;

            Assert.Equal(HttpStatusCode.BadRequest, resultDetails.HttpStatusCode);
            Assert.Equal("error", resultDetails.ErrorMessage);

            _ = mediator.Received().Publish(
                Arg.Is<ImportJobMetricsNotification>(
                    notification => notification.Id == orchestratorJobInfo.Id.ToString() &&
                    notification.Status == JobStatus.Failed.ToString() &&
                    notification.CreatedTime == importOrchestratorJobInputData.CreateTime &&
                    notification.SucceedCount == 0 &&
                    notification.FailedCount == 0),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAnOrchestratorJob_WhenSubJobCancelled_ThenOperationCancelledExceptionShouldBeThrowAndContextUpdated()
        {
            IImportOrchestratorJobDataStoreOperation fhirDataBulkImportOperation = Substitute.For<IImportOrchestratorJobDataStoreOperation>();
            RequestContextAccessor<IFhirRequestContext> contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            ILoggerFactory loggerFactory = new NullLoggerFactory();
            IIntegrationDataStoreClient integrationDataStoreClient = Substitute.For<IIntegrationDataStoreClient>();
            ISequenceIdGenerator<long> sequenceIdGenerator = Substitute.For<ISequenceIdGenerator<long>>();
            IMediator mediator = Substitute.For<IMediator>();
            ImportOrchestratorJobInputData importOrchestratorInputData = new ImportOrchestratorJobInputData();
            TestQueueClient testQueueClient = new TestQueueClient();
            testQueueClient.GetJobByIdFunc = (queueClient, id, _) =>
            {
                JobInfo jobInfo = new JobInfo()
                {
                    Status = JobManagement.JobStatus.Cancelled,
                    Result = JsonConvert.SerializeObject(new ImportProcessingJobErrorResult() { Message = "error" }),
                };

                return jobInfo;
            };

            importOrchestratorInputData.CreateTime = Clock.UtcNow;
            importOrchestratorInputData.BaseUri = new Uri("http://dummy");

            var inputs = new List<InputResource>();
            inputs.Add(new InputResource() { Type = "Resource", Url = new Uri($"http://dummy") });

            importOrchestratorInputData.Input = inputs;
            importOrchestratorInputData.InputFormat = "ndjson";
            importOrchestratorInputData.InputSource = new Uri("http://dummy");
            importOrchestratorInputData.RequestUri = new Uri("http://dummy");

            integrationDataStoreClient.GetPropertiesAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    Dictionary<string, object> properties = new Dictionary<string, object>();
                    properties[IntegrationDataStoreClientConstants.BlobPropertyETag] = "test";
                    properties[IntegrationDataStoreClientConstants.BlobPropertyLength] = 1000L;
                    return properties;
                });

            sequenceIdGenerator.GetCurrentSequenceId().Returns<long>(_ => 0L);
            JobInfo orchestratorJobInfo = (await testQueueClient.EnqueueAsync(0, new string[] { JsonConvert.SerializeObject(importOrchestratorInputData) }, 1, false, false, CancellationToken.None)).First();

            ImportOrchestratorJob orchestratorJob = new ImportOrchestratorJob(
                mediator,
                importOrchestratorInputData,
                new ImportOrchestratorJobResult(),
                contextAccessor,
                fhirDataBulkImportOperation,
                integrationDataStoreClient,
                testQueueClient,
                orchestratorJobInfo,
                new Configs.ImportTaskConfiguration() { MaxRunningProcessingJobCount = 1 },
                loggerFactory);
            orchestratorJob.PollingFrequencyInSeconds = 0;

            var jobExecutionException = await Assert.ThrowsAnyAsync<JobExecutionException>(() => orchestratorJob.ExecuteAsync(new Progress<string>(), CancellationToken.None));
            ImportOrchestratorJobErrorResult resultDetails = (ImportOrchestratorJobErrorResult)jobExecutionException.Error;

            Assert.Equal(HttpStatusCode.BadRequest, resultDetails.HttpStatusCode);

            _ = mediator.Received().Publish(
                Arg.Is<ImportJobMetricsNotification>(
                    notification => notification.Id == orchestratorJobInfo.Id.ToString() &&
                    notification.Status == JobStatus.Cancelled.ToString() &&
                    notification.CreatedTime == importOrchestratorInputData.CreateTime &&
                    notification.SucceedCount == 0 &&
                    notification.FailedCount == 0),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAnOrchestratorJob_WhenSubJobFailed_ThenImportProcessingExceptionShouldBeThrowAndContextUpdated()
        {
            IImportOrchestratorJobDataStoreOperation fhirDataBulkImportOperation = Substitute.For<IImportOrchestratorJobDataStoreOperation>();
            RequestContextAccessor<IFhirRequestContext> contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            ILoggerFactory loggerFactory = new NullLoggerFactory();
            IIntegrationDataStoreClient integrationDataStoreClient = Substitute.For<IIntegrationDataStoreClient>();
            ISequenceIdGenerator<long> sequenceIdGenerator = Substitute.For<ISequenceIdGenerator<long>>();
            IMediator mediator = Substitute.For<IMediator>();
            ImportOrchestratorJobInputData importOrchestratorInputData = new ImportOrchestratorJobInputData();
            TestQueueClient testQueueClient = new TestQueueClient();
            testQueueClient.GetJobByIdFunc = (queueClient, id, _) =>
            {
                JobInfo jobInfo = new JobInfo()
                {
                    Status = JobManagement.JobStatus.Failed,
                    Result = JsonConvert.SerializeObject(new ImportProcessingJobErrorResult() { Message = "error" }),
                };

                return jobInfo;
            };

            importOrchestratorInputData.CreateTime = Clock.UtcNow;
            importOrchestratorInputData.BaseUri = new Uri("http://dummy");

            var inputs = new List<InputResource>();
            inputs.Add(new InputResource() { Type = "Resource", Url = new Uri($"http://dummy") });

            importOrchestratorInputData.Input = inputs;
            importOrchestratorInputData.InputFormat = "ndjson";
            importOrchestratorInputData.InputSource = new Uri("http://dummy");
            importOrchestratorInputData.RequestUri = new Uri("http://dummy");

            integrationDataStoreClient.GetPropertiesAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    Dictionary<string, object> properties = new Dictionary<string, object>();
                    properties[IntegrationDataStoreClientConstants.BlobPropertyETag] = "test";
                    properties[IntegrationDataStoreClientConstants.BlobPropertyLength] = 1000L;
                    return properties;
                });

            sequenceIdGenerator.GetCurrentSequenceId().Returns<long>(_ => 0L);
            JobInfo orchestratorJobInfo = (await testQueueClient.EnqueueAsync(0, new string[] { JsonConvert.SerializeObject(importOrchestratorInputData) }, 1, false, false, CancellationToken.None)).First();

            ImportOrchestratorJob orchestratorJob = new ImportOrchestratorJob(
                mediator,
                importOrchestratorInputData,
                new ImportOrchestratorJobResult(),
                contextAccessor,
                fhirDataBulkImportOperation,
                integrationDataStoreClient,
                testQueueClient,
                orchestratorJobInfo,
                new Configs.ImportTaskConfiguration() { MaxRunningProcessingJobCount = 1 },
                loggerFactory);
            orchestratorJob.PollingFrequencyInSeconds = 0;

            var jobExecutionException = await Assert.ThrowsAnyAsync<JobExecutionException>(() => orchestratorJob.ExecuteAsync(new Progress<string>(), CancellationToken.None));
            ImportOrchestratorJobErrorResult resultDetails = (ImportOrchestratorJobErrorResult)jobExecutionException.Error;

            Assert.Equal(HttpStatusCode.BadRequest, resultDetails.HttpStatusCode);
            Assert.Equal("error", resultDetails.ErrorMessage);

            Assert.True(testQueueClient.JobInfos.All(t => t.Status == JobStatus.Cancelled));

            _ = mediator.Received().Publish(
                Arg.Is<ImportJobMetricsNotification>(
                    notification => notification.Id == orchestratorJobInfo.Id.ToString() &&
                    notification.Status == JobStatus.Failed.ToString() &&
                    notification.CreatedTime == importOrchestratorInputData.CreateTime &&
                    notification.SucceedCount == 0 &&
                    notification.FailedCount == 0),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAnOrchestratorJob_WhenFailedAtPostProcessStep_ThenRetrableExceptionShouldBeThrowAndContextUpdated()
        {
            IImportOrchestratorJobDataStoreOperation fhirDataBulkImportOperation = Substitute.For<IImportOrchestratorJobDataStoreOperation>();
            RequestContextAccessor<IFhirRequestContext> contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            ILoggerFactory loggerFactory = new NullLoggerFactory();
            IIntegrationDataStoreClient integrationDataStoreClient = Substitute.For<IIntegrationDataStoreClient>();
            ISequenceIdGenerator<long> sequenceIdGenerator = Substitute.For<ISequenceIdGenerator<long>>();
            IMediator mediator = Substitute.For<IMediator>();
            ImportOrchestratorJobInputData importOrchestratorJobInputData = new ImportOrchestratorJobInputData();
            List<(long begin, long end)> surrogatedIdRanges = new List<(long begin, long end)>();
            TestQueueClient testQueueClient = new TestQueueClient();
            testQueueClient.GetJobByIdFunc = (testQueueClient, id, _) =>
            {
                JobInfo jobInfo = testQueueClient.JobInfos.First(t => t.Id == id);

                if (jobInfo == null)
                {
                    return null;
                }

                if (jobInfo.Status == JobManagement.JobStatus.Completed)
                {
                    return jobInfo;
                }

                ImportProcessingJobInputData processingInput = JsonConvert.DeserializeObject<ImportProcessingJobInputData>(jobInfo.Definition);
                ImportProcessingJobResult processingResult = new ImportProcessingJobResult();
                processingResult.ResourceType = processingInput.ResourceType;
                processingResult.SucceedCount = 1;
                processingResult.FailedCount = 1;
                processingResult.ErrorLogLocation = "http://dummy/error";
                surrogatedIdRanges.Add((processingInput.BeginSequenceId, processingInput.EndSequenceId));

                jobInfo.Result = JsonConvert.SerializeObject(processingResult);
                jobInfo.Status = JobManagement.JobStatus.Completed;
                return jobInfo;
            };

            importOrchestratorJobInputData.CreateTime = Clock.UtcNow;
            importOrchestratorJobInputData.BaseUri = new Uri("http://dummy");
            var inputs = new List<InputResource>();
            inputs.Add(new InputResource() { Type = "Resource", Url = new Uri($"http://dummy") });

            importOrchestratorJobInputData.Input = inputs;
            importOrchestratorJobInputData.InputFormat = "ndjson";
            importOrchestratorJobInputData.InputSource = new Uri("http://dummy");
            importOrchestratorJobInputData.RequestUri = new Uri("http://dummy");

            integrationDataStoreClient.GetPropertiesAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    Dictionary<string, object> properties = new Dictionary<string, object>();
                    properties[IntegrationDataStoreClientConstants.BlobPropertyETag] = "test";
                    properties[IntegrationDataStoreClientConstants.BlobPropertyLength] = 1000L;
                    return properties;
                });

            fhirDataBulkImportOperation.PostprocessAsync(Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    throw new InvalidCastException();
                });

            JobInfo orchestratorJobInfo = (await testQueueClient.EnqueueAsync(0, new string[] { JsonConvert.SerializeObject(importOrchestratorJobInputData) }, 1, false, false, CancellationToken.None)).First();

            ImportOrchestratorJob orchestratorJob = new ImportOrchestratorJob(
                mediator,
                importOrchestratorJobInputData,
                new ImportOrchestratorJobResult(),
                contextAccessor,
                fhirDataBulkImportOperation,
                integrationDataStoreClient,
                testQueueClient,
                orchestratorJobInfo,
                new Configs.ImportTaskConfiguration() { MaxRunningProcessingJobCount = 1 },
                loggerFactory);
            orchestratorJob.PollingFrequencyInSeconds = 0;

            await Assert.ThrowsAnyAsync<RetriableJobException>(() => orchestratorJob.ExecuteAsync(new Progress<string>(), CancellationToken.None));

            _ = mediator.DidNotReceive().Publish(
                Arg.Any<ImportJobMetricsNotification>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAnOrchestratorJob_WhenCancelledBeforeCompleted_ThenProcessingJobsShouldNotBeCancelled()
        {
            IImportOrchestratorJobDataStoreOperation fhirDataBulkImportOperation = Substitute.For<IImportOrchestratorJobDataStoreOperation>();
            RequestContextAccessor<IFhirRequestContext> contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            ILoggerFactory loggerFactory = new NullLoggerFactory();
            IIntegrationDataStoreClient integrationDataStoreClient = Substitute.For<IIntegrationDataStoreClient>();
            ISequenceIdGenerator<long> sequenceIdGenerator = Substitute.For<ISequenceIdGenerator<long>>();
            IMediator mediator = Substitute.For<IMediator>();
            ImportOrchestratorJobInputData importOrchestratorJobInputData = new ImportOrchestratorJobInputData();
            List<(long begin, long end)> surrogatedIdRanges = new List<(long begin, long end)>();
            TestQueueClient testQueueClient = new TestQueueClient();
            testQueueClient.GetJobByIdFunc = (testQueueClient, id, cancellationToken) =>
            {
                JobInfo jobInfo = testQueueClient.JobInfos.First(t => t.Id == id);

                if (jobInfo == null)
                {
                    return null;
                }

                if (jobInfo.Status == JobManagement.JobStatus.Completed)
                {
                    return jobInfo;
                }

                jobInfo.Status = JobStatus.Running;
                return jobInfo;
            };

            importOrchestratorJobInputData.CreateTime = Clock.UtcNow;
            importOrchestratorJobInputData.BaseUri = new Uri("http://dummy");
            var inputs = new List<InputResource>();
            inputs.Add(new InputResource() { Type = "Resource", Url = new Uri($"http://dummy") });

            importOrchestratorJobInputData.Input = inputs;
            importOrchestratorJobInputData.InputFormat = "ndjson";
            importOrchestratorJobInputData.InputSource = new Uri("http://dummy");
            importOrchestratorJobInputData.RequestUri = new Uri("http://dummy");

            integrationDataStoreClient.GetPropertiesAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    Dictionary<string, object> properties = new Dictionary<string, object>();
                    properties[IntegrationDataStoreClientConstants.BlobPropertyETag] = "test";
                    properties[IntegrationDataStoreClientConstants.BlobPropertyLength] = 1000L;
                    return properties;
                });

            JobInfo orchestratorJobInfo = (await testQueueClient.EnqueueAsync(0, new string[] { JsonConvert.SerializeObject(importOrchestratorJobInputData) }, 1, false, false, CancellationToken.None)).First();

            ImportOrchestratorJob orchestratorJob = new ImportOrchestratorJob(
                mediator,
                importOrchestratorJobInputData,
                new ImportOrchestratorJobResult(),
                contextAccessor,
                fhirDataBulkImportOperation,
                integrationDataStoreClient,
                testQueueClient,
                orchestratorJobInfo,
                new Configs.ImportTaskConfiguration() { MaxRunningProcessingJobCount = 1 },
                loggerFactory);
            orchestratorJob.PollingFrequencyInSeconds = 0;

            CancellationTokenSource cancellationToken = new CancellationTokenSource();
            cancellationToken.CancelAfter(TimeSpan.FromSeconds(1));
            await Assert.ThrowsAnyAsync<JobExecutionException>(() => orchestratorJob.ExecuteAsync(new Progress<string>(), cancellationToken.Token));

            Assert.True(testQueueClient.JobInfos.All(t => t.Status != JobStatus.Cancelled && !t.CancelRequested));
        }

        private static async Task VerifyJobStatusChangedAsync(int inputFileCount, int concurrentCount, JobStatus jobStatus, int succeedCount, int failedCount, int resumeFrom = -1, int completedCount = 0)
        {
            IImportOrchestratorJobDataStoreOperation fhirDataBulkImportOperation = Substitute.For<IImportOrchestratorJobDataStoreOperation>();
            RequestContextAccessor<IFhirRequestContext> contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            ILoggerFactory loggerFactory = new NullLoggerFactory();
            IIntegrationDataStoreClient integrationDataStoreClient = Substitute.For<IIntegrationDataStoreClient>();
            ISequenceIdGenerator<long> sequenceIdGenerator = Substitute.For<ISequenceIdGenerator<long>>();
            IMediator mediator = Substitute.For<IMediator>();
            ImportOrchestratorJobInputData importOrchestratorJobInputData = new ImportOrchestratorJobInputData();
            ImportOrchestratorJobResult importOrchestratorJobResult = new ImportOrchestratorJobResult();

            TestQueueClient testQueueClient = new TestQueueClient();
            List<(long begin, long end)> surrogatedIdRanges = new List<(long begin, long end)>();
            testQueueClient.GetJobByIdFunc = (testQueueClient, id, _) =>
            {
                JobInfo jobInfo = testQueueClient.JobInfos.First(t => t.Id == id);

                if (jobInfo == null)
                {
                    return null;
                }

                if (jobInfo.Status == JobManagement.JobStatus.Completed)
                {
                    return jobInfo;
                }

                if (jobInfo.Id > succeedCount + 1)
                {
                    return new JobInfo()
                    {
                        Id = jobInfo.Id,
                        Status = jobStatus,
                        Result = JsonConvert.SerializeObject(new ImportProcessingJobErrorResult() { Message = "error" }),
                    };
                }

                ImportProcessingJobInputData processingInput = JsonConvert.DeserializeObject<ImportProcessingJobInputData>(jobInfo.Definition);
                ImportProcessingJobResult processingResult = new ImportProcessingJobResult();
                processingResult.ResourceType = processingInput.ResourceType;
                processingResult.SucceedCount = 1;
                processingResult.FailedCount = 1;
                processingResult.ErrorLogLocation = "http://dummy/error";
                surrogatedIdRanges.Add((processingInput.BeginSequenceId, processingInput.EndSequenceId));

                jobInfo.Result = JsonConvert.SerializeObject(processingResult);
                jobInfo.Status = JobManagement.JobStatus.Completed;
                return jobInfo;
            };

            importOrchestratorJobInputData.CreateTime = Clock.UtcNow;
            importOrchestratorJobInputData.BaseUri = new Uri("http://dummy");
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
                        ImportProcessingJobInputData processingInput = new ImportProcessingJobInputData()
                        {
                            ResourceLocation = "http://test",
                            BeginSequenceId = i,
                            EndSequenceId = i + 1,
                        };

                        JobInfo jobInfo = (await testQueueClient.EnqueueAsync(0, new string[] { JsonConvert.SerializeObject(processingInput) }, 1, false, false, CancellationToken.None)).First();

                        ImportProcessingJobResult processingResult = new ImportProcessingJobResult();
                        processingResult.ResourceType = "Resource";
                        processingResult.SucceedCount = 1;
                        processingResult.FailedCount = 1;
                        processingResult.ErrorLogLocation = "http://dummy/error";
                        processingResult.ResourceLocation = location;

                        jobInfo.Result = JsonConvert.SerializeObject(processingResult);
                        if (i < completedCount)
                        {
                            jobInfo.Status = JobManagement.JobStatus.Completed;
                            importOrchestratorJobResult.SucceedImportCount += 1;
                            importOrchestratorJobResult.FailedImportCount += 1;
                        }
                        else
                        {
                            jobInfo.Status = JobManagement.JobStatus.Running;
                            importOrchestratorJobResult.RunningJobIds.Add(jobInfo.Id);
                        }

                        importOrchestratorJobResult.CreatedJobCount += 1;
                        importOrchestratorJobResult.CurrentSequenceId += 1;
                    }

                    importOrchestratorJobResult.Progress = ImportOrchestratorJobProgress.PreprocessCompleted;
                }
            }

            importOrchestratorJobInputData.Input = inputs;
            importOrchestratorJobInputData.InputFormat = "ndjson";
            importOrchestratorJobInputData.InputSource = new Uri("http://dummy");
            importOrchestratorJobInputData.RequestUri = new Uri("http://dummy");
            JobInfo orchestratorJobInfo = (await testQueueClient.EnqueueAsync(0, new string[] { JsonConvert.SerializeObject(importOrchestratorJobInputData) }, 1, false, false, CancellationToken.None)).First();

            integrationDataStoreClient.GetPropertiesAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    Dictionary<string, object> properties = new Dictionary<string, object>();
                    properties[IntegrationDataStoreClientConstants.BlobPropertyETag] = "test";
                    properties[IntegrationDataStoreClientConstants.BlobPropertyLength] = 1000L;
                    return properties;
                });

            sequenceIdGenerator.GetCurrentSequenceId().Returns(_ => 0L);

            ImportOrchestratorJob orchestratorJob = new ImportOrchestratorJob(
                mediator,
                importOrchestratorJobInputData,
                importOrchestratorJobResult,
                contextAccessor,
                fhirDataBulkImportOperation,
                integrationDataStoreClient,
                testQueueClient,
                orchestratorJobInfo,
                new Configs.ImportTaskConfiguration() { MaxRunningProcessingJobCount = concurrentCount },
                loggerFactory);
            orchestratorJob.PollingFrequencyInSeconds = 0;
            var jobExecutionException = await Assert.ThrowsAnyAsync<JobExecutionException>(() => orchestratorJob.ExecuteAsync(new Progress<string>(), CancellationToken.None));
            ImportOrchestratorJobErrorResult resultDetails = (ImportOrchestratorJobErrorResult)jobExecutionException.Error;

            Assert.Equal(HttpStatusCode.BadRequest, resultDetails.HttpStatusCode);
            _ = mediator.Received().Publish(
                Arg.Is<ImportJobMetricsNotification>(
                    notification => notification.Id.Equals(orchestratorJobInfo.Id.ToString()) &&
                    notification.Status == jobStatus.ToString() &&
                    notification.CreatedTime == importOrchestratorJobInputData.CreateTime &&
                    notification.SucceedCount == succeedCount &&
                    notification.FailedCount == failedCount),
                Arg.Any<CancellationToken>());
        }

        private static async Task VerifyCommonOrchestratorJobAsync(int inputFileCount, int concurrentCount, int resumeFrom = -1, int completedCount = 0)
        {
            IImportOrchestratorJobDataStoreOperation fhirDataBulkImportOperation = Substitute.For<IImportOrchestratorJobDataStoreOperation>();
            RequestContextAccessor<IFhirRequestContext> contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            ILoggerFactory loggerFactory = new NullLoggerFactory();
            IIntegrationDataStoreClient integrationDataStoreClient = Substitute.For<IIntegrationDataStoreClient>();
            ISequenceIdGenerator<long> sequenceIdGenerator = Substitute.For<ISequenceIdGenerator<long>>();
            IMediator mediator = Substitute.For<IMediator>();
            ImportOrchestratorJobInputData importOrchestratorJobInputData = new ImportOrchestratorJobInputData();
            ImportOrchestratorJobResult importOrchestratorJobResult = new ImportOrchestratorJobResult();

            TestQueueClient testQueueClient = new TestQueueClient();
            List<(long begin, long end)> surrogatedIdRanges = new List<(long begin, long end)>();
            testQueueClient.GetJobByIdFunc = (testQueueClient, id, _) =>
            {
                JobInfo jobInfo = testQueueClient.JobInfos.First(t => t.Id == id);

                if (jobInfo == null)
                {
                    return null;
                }

                if (jobInfo.Status == JobManagement.JobStatus.Completed)
                {
                    return jobInfo;
                }

                ImportProcessingJobInputData processingInput = JsonConvert.DeserializeObject<ImportProcessingJobInputData>(jobInfo.Definition);
                ImportProcessingJobResult processingResult = new ImportProcessingJobResult();
                processingResult.ResourceType = processingInput.ResourceType;
                processingResult.SucceedCount = 1;
                processingResult.FailedCount = 1;
                processingResult.ErrorLogLocation = "http://dummy/error";
                surrogatedIdRanges.Add((processingInput.BeginSequenceId, processingInput.EndSequenceId));

                jobInfo.Result = JsonConvert.SerializeObject(processingResult);
                jobInfo.Status = JobManagement.JobStatus.Completed;
                return jobInfo;
            };

            importOrchestratorJobInputData.CreateTime = Clock.UtcNow;
            importOrchestratorJobInputData.BaseUri = new Uri("http://dummy");
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
                        ImportProcessingJobInputData processingInput = new ImportProcessingJobInputData()
                        {
                            ResourceLocation = "http://test",
                            BeginSequenceId = i,
                            EndSequenceId = i + 1,
                        };

                        JobInfo jobInfo = (await testQueueClient.EnqueueAsync(0, new string[] { JsonConvert.SerializeObject(processingInput) }, 1, false, false, CancellationToken.None)).First();

                        ImportProcessingJobResult processingResult = new ImportProcessingJobResult();
                        processingResult.ResourceType = "Resource";
                        processingResult.SucceedCount = 1;
                        processingResult.FailedCount = 1;
                        processingResult.ErrorLogLocation = "http://dummy/error";
                        processingResult.ResourceLocation = location;

                        jobInfo.Result = JsonConvert.SerializeObject(processingResult);
                        if (i < completedCount)
                        {
                            jobInfo.Status = JobManagement.JobStatus.Completed;
                            importOrchestratorJobResult.SucceedImportCount += 1;
                            importOrchestratorJobResult.FailedImportCount += 1;
                        }
                        else
                        {
                            jobInfo.Status = JobManagement.JobStatus.Running;
                            importOrchestratorJobResult.RunningJobIds.Add(jobInfo.Id);
                        }

                        importOrchestratorJobResult.CreatedJobCount += 1;
                        importOrchestratorJobResult.CurrentSequenceId += 1;
                    }

                    importOrchestratorJobResult.Progress = ImportOrchestratorJobProgress.PreprocessCompleted;
                }
            }

            importOrchestratorJobInputData.Input = inputs;
            importOrchestratorJobInputData.InputFormat = "ndjson";
            importOrchestratorJobInputData.InputSource = new Uri("http://dummy");
            importOrchestratorJobInputData.RequestUri = new Uri("http://dummy");
            JobInfo orchestratorJobInfo = (await testQueueClient.EnqueueAsync(0, new string[] { JsonConvert.SerializeObject(importOrchestratorJobInputData) }, 1, false, false, CancellationToken.None)).First();

            integrationDataStoreClient.GetPropertiesAsync(Arg.Any<Uri>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    Dictionary<string, object> properties = new Dictionary<string, object>();
                    properties[IntegrationDataStoreClientConstants.BlobPropertyETag] = "test";
                    properties[IntegrationDataStoreClientConstants.BlobPropertyLength] = 1000L;
                    return properties;
                });

            sequenceIdGenerator.GetCurrentSequenceId().Returns(_ => 0L);

            ImportOrchestratorJob orchestratorJob = new ImportOrchestratorJob(
                mediator,
                importOrchestratorJobInputData,
                importOrchestratorJobResult,
                contextAccessor,
                fhirDataBulkImportOperation,
                integrationDataStoreClient,
                testQueueClient,
                orchestratorJobInfo,
                new Configs.ImportTaskConfiguration() { MaxRunningProcessingJobCount = concurrentCount },
                loggerFactory);
            orchestratorJob.PollingFrequencyInSeconds = 0;

            string result = await orchestratorJob.ExecuteAsync(new Progress<string>(), CancellationToken.None);
            ImportOrchestratorJobResult resultDetails = JsonConvert.DeserializeObject<ImportOrchestratorJobResult>(result);
            Assert.NotEmpty(resultDetails.Request);
            Assert.Equal(importOrchestratorJobInputData.CreateTime, resultDetails.TransactionTime);

            Assert.Equal(inputFileCount, testQueueClient.JobInfos.Count() - 1);

            var orderedSurrogatedIdRanges = surrogatedIdRanges.OrderBy(r => r.begin).ToArray();
            Assert.Equal(inputFileCount, orderedSurrogatedIdRanges.Length + completedCount);
            for (int i = 0; i < orderedSurrogatedIdRanges.Length - 1; ++i)
            {
                Assert.True(orderedSurrogatedIdRanges[i].end > orderedSurrogatedIdRanges[i].begin);
                Assert.True(orderedSurrogatedIdRanges[i].end <= orderedSurrogatedIdRanges[i + 1].begin);
            }

            _ = mediator.Received().Publish(
                Arg.Is<ImportJobMetricsNotification>(
                    notification => notification.Id.Equals(orchestratorJobInfo.Id.ToString()) &&
                    notification.Status == JobStatus.Completed.ToString() &&
                    notification.CreatedTime == importOrchestratorJobInputData.CreateTime &&
                    notification.SucceedCount == inputFileCount &&
                    notification.FailedCount == inputFileCount),
                Arg.Any<CancellationToken>());
        }
    }
}
