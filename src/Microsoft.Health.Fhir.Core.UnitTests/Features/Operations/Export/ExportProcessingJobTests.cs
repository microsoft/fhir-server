// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
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
            string progressResult = string.Empty;

            Progress<string> progress = new Progress<string>((result) =>
            {
                progressResult = result;
            });

            var expectedResults = GenerateJobRecord(OperationStatus.Completed);

            var processingJob = new ExportProcessingJob(MakeMockJob, new TestQueueClient());
            var taskResult = await processingJob.ExecuteAsync(GenerateJobInfo(expectedResults), progress, CancellationToken.None);
            Assert.Equal(expectedResults, taskResult);

            // For some reason checking the progress result is flaky. Sometimes it passes and sometimes it is blank. There seems to be a timing error, but debugging it causes it not to happen.
            // All the steps are synchronous, so I don't see where the issue is happening.
            // Assert.Equal(expectedResults, progressResult);
        }

        [Fact]
        public async Task GivenAnExportJob_WhenItFails_ThenAnExceptionIsThrown()
        {
            Progress<string> progress = new Progress<string>((result) => { });

            var exceptionMessage = "Test job failed";
            var expectedResults = GenerateJobRecord(OperationStatus.Failed, exceptionMessage);

            var processingJob = new ExportProcessingJob(new Func<IExportJobTask>(MakeMockJob), new TestQueueClient());
            var exception = await Assert.ThrowsAsync<JobExecutionException>(() => processingJob.ExecuteAsync(GenerateJobInfo(expectedResults), progress, CancellationToken.None));
            Assert.Equal(exceptionMessage, exception.Message);
        }

        [Fact]
        public async Task GivenAnExportJob_WhenItIsCancelled_ThenAnExceptionIsThrown()
        {
            Progress<string> progress = new Progress<string>((result) => { });

            var expectedResults = GenerateJobRecord(OperationStatus.Canceled);

            var processingJob = new ExportProcessingJob(new Func<IExportJobTask>(MakeMockJob), new TestQueueClient());
            await Assert.ThrowsAsync<RetriableJobException>(() => processingJob.ExecuteAsync(GenerateJobInfo(expectedResults), progress, CancellationToken.None));
        }

        [Theory]
        [InlineData(OperationStatus.Queued)]
        [InlineData(OperationStatus.Running)]
        public async Task GivenAnExportJob_WhenItFinishesInANonTerminalState_ThenAnExceptionIsThrown(OperationStatus status)
        {
            Progress<string> progress = new Progress<string>((result) => { });

            var expectedResults = GenerateJobRecord(status);

            var processingJob = new ExportProcessingJob(new Func<IExportJobTask>(MakeMockJobThatReturnsImmediately), new TestQueueClient());
            await Assert.ThrowsAsync<RetriableJobException>(() => processingJob.ExecuteAsync(GenerateJobInfo(expectedResults), progress, CancellationToken.None));
        }

        [Fact]
        public async Task GivenAnExportJob_WhenItFinishesAPageOfResults_ThenANewProgressJobIsQueued()
        {
            string progressResult = string.Empty;

            Progress<string> progress = new Progress<string>((result) =>
            {
                progressResult = result;
            });

            var expectedResults = GenerateJobRecord(OperationStatus.Running);

            var queueClient = new TestQueueClient();
            var processingJob = new ExportProcessingJob(MakeMockJobWithProgressUpdate, queueClient);
            var taskResult = await processingJob.ExecuteAsync(GenerateJobInfo(expectedResults), progress, CancellationToken.None);

            Assert.Single(queueClient.JobInfos);
            Assert.Contains(_progressToken, queueClient.JobInfos[0].Definition);
        }

        [Fact]
        public async Task GivenAnExportJob_WhenItFinishesAPageOfResultsAndAFollowupJobExists_ThenANewProgressJobIsNotQueued()
        {
            string progressResult = string.Empty;

            Progress<string> progress = new Progress<string>((result) =>
            {
                progressResult = result;
            });

            var expectedResults = GenerateJobRecord(OperationStatus.Running);

            var queueClient = new TestQueueClient();
            var processingJob = new ExportProcessingJob(MakeMockJobWithProgressUpdate, queueClient);

            var runningJob = GenerateJobInfo(expectedResults);
            var followUpJob = GenerateJobInfo(expectedResults);
            followUpJob.Id = runningJob.Id + 1;
            queueClient.JobInfos.Add(followUpJob);

            var taskResult = await processingJob.ExecuteAsync(runningJob, progress, CancellationToken.None);

            Assert.Single(queueClient.JobInfos);
            Assert.DoesNotContain(_progressToken, queueClient.JobInfos[0].Definition);
            Assert.Equal(followUpJob, queueClient.JobInfos[0]);
        }

        [Theory]
        [InlineData("Patient", "Observation", null, null)]
        [InlineData(null, null, "range1", "range2")]
        public async Task GivenAnExportJob_WhenItFinishesAPageOfResultsAndNewerParallelJobExists_ThenANewProgressJobIsQueued(string resourceType1, string resourceType2, string feedRange1, string feedRange2)
        {
            string progressResult = string.Empty;

            Progress<string> progress = new Progress<string>((result) =>
            {
                progressResult = result;
            });

            var queueClient = new TestQueueClient();
            var processingJob = new ExportProcessingJob(MakeMockJobWithProgressUpdate, queueClient);

            var runningJob = GenerateJobInfo(GenerateJobRecord(OperationStatus.Running, resourceType: resourceType1, feedRange: feedRange1));

            var followUpJob = GenerateJobInfo(GenerateJobRecord(OperationStatus.Running, resourceType: resourceType2, feedRange: feedRange2));
            followUpJob.Id = runningJob.Id + 1;
            queueClient.JobInfos.Add(followUpJob);

            var taskResult = await processingJob.ExecuteAsync(runningJob, progress, CancellationToken.None);

            Assert.True(queueClient.JobInfos.Count == 2);
            Assert.DoesNotContain(_progressToken, queueClient.JobInfos[0].Definition);
            Assert.Equal(followUpJob, queueClient.JobInfos[0]);
        }

        [Fact]
        public async Task GivenAnExportJob_WhenItFinishesAPageOfResultAndCanceledJobInQueue_ThenANewProgressJobIsNotQueued()
        {
            string progressResult = string.Empty;

            Progress<string> progress = new Progress<string>((result) =>
            {
                progressResult = result;
            });

            var queueClient = new TestQueueClient();
            var processingJob = new ExportProcessingJob(MakeMockJobWithProgressUpdate, queueClient);

            var runningJob = GenerateJobInfo(GenerateJobRecord(OperationStatus.Running));

            var existingCanceledJob = GenerateJobInfo(GenerateJobRecord(OperationStatus.Running));
            existingCanceledJob.Id = runningJob.Id - 2;
            existingCanceledJob.Status = JobStatus.Cancelled;
            queueClient.JobInfos.Add(existingCanceledJob);

            var existingCanceledPendingJob = GenerateJobInfo(GenerateJobRecord(OperationStatus.Running));
            existingCanceledPendingJob.Id = runningJob.Id - 1;
            existingCanceledPendingJob.CancelRequested = true;
            queueClient.JobInfos.Add(existingCanceledPendingJob);

            var taskResult = await processingJob.ExecuteAsync(runningJob, progress, CancellationToken.None);

            Assert.True(queueClient.JobInfos.Count == 2);
            Assert.DoesNotContain(_progressToken, queueClient.JobInfos[0].Definition);
            Assert.DoesNotContain(_progressToken, queueClient.JobInfos[1].Definition);
            Assert.Equal(existingCanceledJob, queueClient.JobInfos[0]);
            Assert.Equal(existingCanceledPendingJob, queueClient.JobInfos[1]);
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
