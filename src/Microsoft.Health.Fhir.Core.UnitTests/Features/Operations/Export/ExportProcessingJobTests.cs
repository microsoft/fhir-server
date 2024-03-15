// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.JobManagement.UnitTests;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Export
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Export)]
    public class ExportProcessingJobTests
    {
        private readonly string _progressToken = "progress token";

        [Fact]
        public async Task GivenAnExportJob_WhenItSucceeds_ThenOutputsAreInTheResult()
        {
            var expectedResults = GenerateJobRecord(OperationStatus.Completed);

            var processingJob = new ExportProcessingJob(MakeMockJob, new TestQueueClient(), new NullLogger<ExportProcessingJob>());
            var taskResult = await processingJob.ExecuteAsync(GenerateJobInfo(expectedResults), CancellationToken.None);
            Assert.Equal(expectedResults, taskResult);
        }

        [Fact]
        public async Task GivenAnExportJob_WhenItFails_ThenAnExceptionIsThrown()
        {
            var exceptionMessage = "Test job failed";
            var expectedResults = GenerateJobRecord(OperationStatus.Failed, exceptionMessage);

            var processingJob = new ExportProcessingJob(new Func<IExportJobTask>(MakeMockJob), new TestQueueClient(), new NullLogger<ExportProcessingJob>());
            var exception = await Assert.ThrowsAsync<JobExecutionException>(() => processingJob.ExecuteAsync(GenerateJobInfo(expectedResults), CancellationToken.None));
            Assert.Equal(exceptionMessage, exception.Message);
        }

        [Fact]
        public async Task GivenAnExportJob_WhenItIsCancelled_ThenAnExceptionIsThrown()
        {
            var expectedResults = GenerateJobRecord(OperationStatus.Canceled);

            var processingJob = new ExportProcessingJob(new Func<IExportJobTask>(MakeMockJob), new TestQueueClient(), new NullLogger<ExportProcessingJob>());
            await Assert.ThrowsAsync<RetriableJobException>(() => processingJob.ExecuteAsync(GenerateJobInfo(expectedResults), CancellationToken.None));
        }

        [Theory]
        [InlineData(OperationStatus.Queued)]
        [InlineData(OperationStatus.Running)]
        public async Task GivenAnExportJob_WhenItFinishesInANonTerminalState_ThenAnExceptionIsThrown(OperationStatus status)
        {
            var expectedResults = GenerateJobRecord(status);

            var processingJob = new ExportProcessingJob(new Func<IExportJobTask>(MakeMockJobThatReturnsImmediately), new TestQueueClient(), new NullLogger<ExportProcessingJob>());
            await Assert.ThrowsAsync<RetriableJobException>(() => processingJob.ExecuteAsync(GenerateJobInfo(expectedResults), CancellationToken.None));
        }

        [Fact]
        public async Task GivenAnExportJob_WhenItFinishesAPageOfResults_ThenANewProgressJobIsQueued()
        {
            var expectedResults = GenerateJobRecord(OperationStatus.Running);

            var queueClient = new TestQueueClient();
            var processingJob = new ExportProcessingJob(MakeMockJobWithProgressUpdate, queueClient, new NullLogger<ExportProcessingJob>());
            await processingJob.ExecuteAsync(GenerateJobInfo(expectedResults), CancellationToken.None);

            Assert.Single(queueClient.JobInfos);
            Assert.Contains(_progressToken, queueClient.JobInfos[0].Definition);
        }

        [Fact]
        public async Task GivenAnExportJob_WhenItFinishesAPageOfResultsAndAFollowupJobExists_ThenANewProgressJobIsNotQueued()
        {
            var expectedResults = GenerateJobRecord(OperationStatus.Running);

            var queueClient = new TestQueueClient();
            var processingJob = new ExportProcessingJob(MakeMockJobWithProgressUpdate, queueClient, new NullLogger<ExportProcessingJob>());

            var runningJob = GenerateJobInfo(expectedResults);
            var followUpJob = GenerateJobInfo(expectedResults);
            followUpJob.Id = runningJob.Id + 1;
            queueClient.JobInfos.Add(followUpJob);

            await processingJob.ExecuteAsync(runningJob, CancellationToken.None);

            Assert.Single(queueClient.JobInfos);
            Assert.DoesNotContain(_progressToken, queueClient.JobInfos[0].Definition);
            Assert.Equal(followUpJob, queueClient.JobInfos[0]);
        }

        [Theory]
        [InlineData("Patient", "Observation", null, null)]
        [InlineData(null, null, "range1", "range2")]
        public async Task GivenAnExportJob_WhenItFinishesAPageAndNewerParallelJobExists_ThenANewProgressJobIsQueued(string testRunningJobResourceType, string laterParallelJobResourceType, string testRunningJobFeedRange, string laterParallelJobFeedRange)
        {
            var queueClient = new TestQueueClient();
            var processingJob = new ExportProcessingJob(MakeMockJobWithProgressUpdate, queueClient, new NullLogger<ExportProcessingJob>());

            // Note: Feed ranges are different which means testRunningJob should queue the next job even though.
            var testRunningJob = GenerateJobInfo(GenerateJobRecord(OperationStatus.Running, resourceType: testRunningJobResourceType, feedRange: testRunningJobFeedRange));
            var laterParallelRunningJob = GenerateJobInfo(GenerateJobRecord(OperationStatus.Running, resourceType: laterParallelJobResourceType, feedRange: laterParallelJobFeedRange));

            testRunningJob.Id = 1;
            laterParallelRunningJob.Id = 2;
            queueClient.JobInfos.Add(laterParallelRunningJob);

            await processingJob.ExecuteAsync(testRunningJob, CancellationToken.None);

            Assert.True(queueClient.JobInfos.Count == 2); // laterParallelRunningJob + follow up job for testRunningJob.
            Assert.Equal(laterParallelRunningJob, queueClient.JobInfos[0]);

            var followUpJobDefinition = queueClient.JobInfos[1].DeserializeDefinition<ExportJobRecord>();

            Assert.Equal(testRunningJobResourceType, followUpJobDefinition.ResourceType);
            Assert.Equal(testRunningJobFeedRange, followUpJobDefinition.FeedRange);
            Assert.Contains(_progressToken, queueClient.JobInfos[1].Definition); // This is a follow up job, not our orig testRunningJob.
        }

        [Theory]
        [InlineData(JobStatus.Cancelled, false)]
        [InlineData(JobStatus.Running, true)]
        public async Task GivenAnExportJob_WhenItFinishesAPageOfResultAndCanceledGroupJobInQueue_ThenANewProgressJobIsNotQueued(JobStatus existingJobStatus, bool cancellationRequested)
        {
            var queueClient = new TestQueueClient();
            var processingJob = new ExportProcessingJob(MakeMockJobWithProgressUpdate, queueClient, new NullLogger<ExportProcessingJob>());

            var runningJob = GenerateJobInfo(GenerateJobRecord(OperationStatus.Running));

            var existingCanceledJob = GenerateJobInfo(GenerateJobRecord(OperationStatus.Running));
            existingCanceledJob.Id = runningJob.Id - 1;
            existingCanceledJob.Status = existingJobStatus;
            existingCanceledJob.CancelRequested = cancellationRequested;
            queueClient.JobInfos.Add(existingCanceledJob);

            await processingJob.ExecuteAsync(runningJob, CancellationToken.None);

            Assert.True(queueClient.JobInfos.Count == 1);
            Assert.DoesNotContain(_progressToken, queueClient.JobInfos[0].Definition);
            Assert.Equal(existingCanceledJob, queueClient.JobInfos[0]);
        }

        private string GenerateJobRecord(OperationStatus status, string failureReason = null, string resourceType = null, string feedRange = null)
        {
            var record = new ExportJobRecord(
                requestUri: new Uri("https://localhost/ExportJob/"),
                exportType: ExportJobType.All,
                exportFormat: ExportFormatTags.ResourceName,
                resourceType: resourceType,
                filters: null,
                hash: "hash",
                rollingFileSizeInMB: 0,
                feedRange: feedRange);
            record.Status = status;
            if (failureReason != null)
            {
                record.FailureDetails = new JobFailureDetails(failureReason, HttpStatusCode.InternalServerError);
            }

            record.Id = string.Empty;
            return JsonConvert.SerializeObject(record);
        }

        private JobInfo GenerateJobInfo(string record)
        {
            var info = new JobInfo();
            info.Id = RandomNumberGenerator.GetInt32(int.MaxValue);
            info.Definition = record;
            return info;
        }

        private IExportJobTask MakeMockJob()
        {
            var mockJob = Substitute.For<IExportJobTask>();
            mockJob.ExecuteAsync(Arg.Any<ExportJobRecord>(), Arg.Any<WeakETag>(), Arg.Any<CancellationToken>()).Returns(x =>
            {
                return mockJob.UpdateExportJob(x.ArgAt<ExportJobRecord>(0), x.ArgAt<WeakETag>(1), x.ArgAt<CancellationToken>(2));
            });

            return mockJob;
        }

        private IExportJobTask MakeMockJobThatReturnsImmediately()
        {
            var mockJob = Substitute.For<IExportJobTask>();
            mockJob.ExecuteAsync(Arg.Any<ExportJobRecord>(), Arg.Any<WeakETag>(), Arg.Any<CancellationToken>()).Returns(x =>
            {
                return Task.FromResult(new ExportJobOutcome(x.ArgAt<ExportJobRecord>(0), x.ArgAt<WeakETag>(1)));
            });

            return mockJob;
        }

        private IExportJobTask MakeMockJobWithProgressUpdate()
        {
            var mockJob = Substitute.For<IExportJobTask>();
            mockJob.ExecuteAsync(Arg.Any<ExportJobRecord>(), Arg.Any<WeakETag>(), Arg.Any<CancellationToken>()).Returns(async x =>
            {
                var record = x.ArgAt<ExportJobRecord>(0);
                record.Progress = new ExportJobProgress(_progressToken, 1);
                try
                {
                    await mockJob.UpdateExportJob(record, x.ArgAt<WeakETag>(1), x.ArgAt<CancellationToken>(2));
                }
                catch (JobSegmentCompletedException)
                {
                    record.Status = OperationStatus.Completed;
                    await mockJob.UpdateExportJob(record, x.ArgAt<WeakETag>(1), x.ArgAt<CancellationToken>(2));
                }
            });

            return mockJob;
        }
    }
}
