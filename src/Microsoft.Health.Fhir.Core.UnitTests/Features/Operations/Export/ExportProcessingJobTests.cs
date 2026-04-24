// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
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

        private static Func<IScoped<ISearchService>> CreateMockSearchServiceFactory()
        {
            var searchService = Substitute.For<ISearchService>();
            var scoped = Substitute.For<IScoped<ISearchService>>();
            scoped.Value.Returns(searchService);
            return () => scoped;
        }

        [Fact]
        public async Task GivenAnExportJob_WhenItSucceeds_ThenOutputsAreInTheResult()
        {
            var expectedResults = GenerateJobRecord(OperationStatus.Completed);

            var processingJob = new ExportProcessingJob(MakeMockJob, CreateMockSearchServiceFactory(), new TestQueueClient(), new NullLogger<ExportProcessingJob>());
            var taskResult = await processingJob.ExecuteAsync(GenerateJobInfo(expectedResults), CancellationToken.None);
            Assert.Equal(expectedResults, taskResult);
        }

        [Fact]
        public async Task GivenAnExportJob_WhenItFails_ThenAnExceptionIsThrown()
        {
            var exceptionMessage = "Test job failed";
            var expectedResults = GenerateJobRecord(OperationStatus.Failed, exceptionMessage);

            var processingJob = new ExportProcessingJob(new Func<IExportJobTask>(MakeMockJob), CreateMockSearchServiceFactory(), new TestQueueClient(), new NullLogger<ExportProcessingJob>());
            var exception = await Assert.ThrowsAsync<JobExecutionException>(() => processingJob.ExecuteAsync(GenerateJobInfo(expectedResults), CancellationToken.None));
            Assert.Equal(exceptionMessage, exception.Message);
        }

        [Fact]
        public async Task GivenAnExportJob_WhenItIsCancelled_ThenAnExceptionIsThrown()
        {
            var expectedResults = GenerateJobRecord(OperationStatus.Canceled);

            var processingJob = new ExportProcessingJob(new Func<IExportJobTask>(MakeMockJob), CreateMockSearchServiceFactory(), new TestQueueClient(), new NullLogger<ExportProcessingJob>());
            await Assert.ThrowsAsync<OperationCanceledException>(() => processingJob.ExecuteAsync(GenerateJobInfo(expectedResults), CancellationToken.None));
        }

        [Theory]
        [InlineData(OperationStatus.Queued)]
        [InlineData(OperationStatus.Running)]
        public async Task GivenAnExportJob_WhenItFinishesInANonTerminalState_ThenAnExceptionIsThrown(OperationStatus status)
        {
            var expectedResults = GenerateJobRecord(status);

            var processingJob = new ExportProcessingJob(new Func<IExportJobTask>(MakeMockJobThatReturnsImmediately), CreateMockSearchServiceFactory(), new TestQueueClient(), new NullLogger<ExportProcessingJob>());
            await Assert.ThrowsAsync<JobExecutionException>(() => processingJob.ExecuteAsync(GenerateJobInfo(expectedResults), CancellationToken.None));
        }

        [Fact]
        public async Task GivenAnExportJob_WhenItFinishesAPageOfResults_ThenANewProgressJobIsQueued()
        {
            var expectedResults = GenerateJobRecord(OperationStatus.Running);

            var queueClient = new TestQueueClient();
            var processingJob = new ExportProcessingJob(MakeMockJobWithProgressUpdate, CreateMockSearchServiceFactory(), queueClient, new NullLogger<ExportProcessingJob>());
            await processingJob.ExecuteAsync(GenerateJobInfo(expectedResults), CancellationToken.None);

            Assert.Single(queueClient.JobInfos);
            Assert.Contains(_progressToken, queueClient.JobInfos[0].Definition);
        }

        [Fact]
        public async Task GivenAnExportJob_WhenItFinishesAPageOfResultsAndAFollowupJobExists_ThenANewProgressJobIsNotQueued()
        {
            var expectedResults = GenerateJobRecord(OperationStatus.Running);

            var queueClient = new TestQueueClient();
            var processingJob = new ExportProcessingJob(MakeMockJobWithProgressUpdate, CreateMockSearchServiceFactory(), queueClient, new NullLogger<ExportProcessingJob>());

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
            var processingJob = new ExportProcessingJob(MakeMockJobWithProgressUpdate, CreateMockSearchServiceFactory(), queueClient, new NullLogger<ExportProcessingJob>());

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
            var processingJob = new ExportProcessingJob(MakeMockJobWithProgressUpdate, CreateMockSearchServiceFactory(), queueClient, new NullLogger<ExportProcessingJob>());

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

        private string GenerateJobRecord(OperationStatus status, string failureReason = null, string resourceType = null, string feedRange = null, string startSurrogateId = null, string endSurrogateId = null, ExportJobType exportJobType = ExportJobType.All, string groupId = null, uint maximumNumberOfResourcesPerQuery = 100)
        {
            var record = new ExportJobRecord(
                requestUri: new Uri("https://localhost/ExportJob/"),
                exportType: exportJobType,
                exportFormat: ExportFormatTags.ResourceName,
                resourceType: resourceType,
                filters: null,
                hash: "hash",
                rollingFileSizeInMB: 0,
                feedRange: feedRange,
                groupId: groupId,
                maximumNumberOfResourcesPerQuery: maximumNumberOfResourcesPerQuery);
            record.Status = status;
            if (failureReason != null)
            {
                record.FailureDetails = new JobFailureDetails(failureReason, HttpStatusCode.InternalServerError);
            }

            if (startSurrogateId != null)
            {
                record.StartSurrogateId = startSurrogateId;
            }

            if (endSurrogateId != null)
            {
                record.EndSurrogateId = endSurrogateId;
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

        [Theory]
        [InlineData(null, null)]
        [InlineData("100", "200")]
        public async Task GivenExportJob_WhenOomExceedsMaxReductions_ThenThrows(string startSurrogateId, string endSurrogateId)
        {
            IExportJobTask MakeMockJobThatAlwaysOoms()
            {
                var mockJob = Substitute.For<IExportJobTask>();
                mockJob.ExecuteAsync(Arg.Any<ExportJobRecord>(), Arg.Any<WeakETag>(), Arg.Any<CancellationToken>()).Returns<Task>(x =>
                {
                    throw new OutOfMemoryException("Persistent OOM");
                });

                return mockJob;
            }

            var searchService = Substitute.For<ISearchService>();
            searchService.GetSurrogateIdRanges(
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<long>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>()).Returns(new List<(long StartId, long EndId, int Count)>
                {
                    (100, 150, 50),
                    (150, 200, 50),
                });

            var scoped = Substitute.For<IScoped<ISearchService>>();
            scoped.Value.Returns(searchService);
            Func<IScoped<ISearchService>> factory = () => scoped;

            var processingJob = new ExportProcessingJob(MakeMockJobThatAlwaysOoms, factory, new TestQueueClient(), new NullLogger<ExportProcessingJob>());
            var jobRecord = GenerateJobRecord(OperationStatus.Completed, resourceType: "Patient", startSurrogateId: startSurrogateId, endSurrogateId: endSurrogateId);
            var jobInfo = GenerateJobInfo(jobRecord);

            var ex = await Assert.ThrowsAsync<JobExecutionException>(() => processingJob.ExecuteAsync(jobInfo, CancellationToken.None));
            var failedRecord = (ExportJobRecord)ex.Error;
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, failedRecord.FailureDetails.FailureStatusCode);
        }

        [Theory]
        [InlineData(null, null)]
        [InlineData("100", "200")]
        public async Task GivenExportJob_WhenOomOccurs_ThenBatchSizeIsReduced(string startSurrogateId, string endSurrogateId)
        {
            uint capturedBatchSize = 0;
            int callCount = 0;
            IExportJobTask MakeMockJobCapturingBatchSize()
            {
                var mockJob = Substitute.For<IExportJobTask>();
                mockJob.ExecuteAsync(Arg.Any<ExportJobRecord>(), Arg.Any<WeakETag>(), Arg.Any<CancellationToken>()).Returns(async x =>
                {
                    callCount++;
                    var record = x.ArgAt<ExportJobRecord>(0);

                    if (callCount == 1)
                    {
                        throw new OutOfMemoryException("Simulated OOM");
                    }

                    capturedBatchSize = record.MaximumNumberOfResourcesPerQuery;
                    record.Status = OperationStatus.Completed;
                    await mockJob.UpdateExportJob(record, x.ArgAt<WeakETag>(1), x.ArgAt<CancellationToken>(2));
                });

                return mockJob;
            }

            var searchService = Substitute.For<ISearchService>();
            searchService.GetSurrogateIdRanges(
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<long>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>()).Returns(new List<(long StartId, long EndId, int Count)>
                {
                    (100, 200, 100),
                });

            var scoped = Substitute.For<IScoped<ISearchService>>();
            scoped.Value.Returns(searchService);
            Func<IScoped<ISearchService>> factory = () => scoped;

            var processingJob = new ExportProcessingJob(MakeMockJobCapturingBatchSize, factory, new TestQueueClient(), new NullLogger<ExportProcessingJob>());
            var jobRecord = GenerateJobRecord(OperationStatus.Completed, resourceType: "Patient", startSurrogateId: startSurrogateId, endSurrogateId: endSurrogateId);
            var jobInfo = GenerateJobInfo(jobRecord);

            await processingJob.ExecuteAsync(jobInfo, CancellationToken.None);

            // Default MaximumNumberOfResourcesPerQuery is 100, reduced by factor of 10 = 10
            Assert.Equal(10u, capturedBatchSize);
        }

        [Theory]
        [InlineData(ExportJobType.Patient, null)]
        [InlineData(ExportJobType.Group, "group")]
        [InlineData(ExportJobType.All, null)]
        public async Task GivenExportJobOfAnyType_WhenOomOccurs_ThenReducesBatchSizeAndRetries(ExportJobType exportJobType, string groupId)
        {
            int callCount = 0;
            IExportJobTask MakeMockJobWithOomThenSuccess()
            {
                var mockJob = Substitute.For<IExportJobTask>();
                mockJob.ExecuteAsync(Arg.Any<ExportJobRecord>(), Arg.Any<WeakETag>(), Arg.Any<CancellationToken>()).Returns(async x =>
                {
                    callCount++;
                    var record = x.ArgAt<ExportJobRecord>(0);

                    if (callCount == 1)
                    {
                        throw new OutOfMemoryException("Simulated OOM");
                    }

                    record.Status = OperationStatus.Completed;
                    await mockJob.UpdateExportJob(record, x.ArgAt<WeakETag>(1), x.ArgAt<CancellationToken>(2));
                });

                return mockJob;
            }

            var processingJob = new ExportProcessingJob(MakeMockJobWithOomThenSuccess, CreateMockSearchServiceFactory(), new TestQueueClient(), new NullLogger<ExportProcessingJob>());
            var jobInfo = GenerateJobInfo(GenerateJobRecord(OperationStatus.Completed, exportJobType: exportJobType, groupId: groupId));

            var result = await processingJob.ExecuteAsync(jobInfo, CancellationToken.None);

            Assert.Equal(2, callCount);
            Assert.NotNull(result);
        }

        [Theory]
        [InlineData(ExportJobType.Patient, "Condition,Observation", "Patient", "100", "200")] // Patient export: _type filter is Condition,Observation but GetSurrogateIdRanges should use "Patient"
        [InlineData(ExportJobType.All, "Observation", "Observation", "500", "1000")] // All export: single resource type from orchestrator
        public async Task GivenSqlExportJobOfAnyType_WhenOomOccurs_ThenSplitsRangeAndRetries(ExportJobType exportJobType, string resourceType, string expectedRangeResourceType, string startId, string endId)
        {
            int callCount = 0;
            IExportJobTask MakeMockJobWithOomOnFirstRange()
            {
                var mockJob = Substitute.For<IExportJobTask>();
                mockJob.ExecuteAsync(Arg.Any<ExportJobRecord>(), Arg.Any<WeakETag>(), Arg.Any<CancellationToken>()).Returns(async x =>
                {
                    callCount++;
                    var record = x.ArgAt<ExportJobRecord>(0);

                    if (callCount == 1)
                    {
                        throw new OutOfMemoryException("Simulated OOM on SQL path");
                    }

                    record.Status = OperationStatus.Completed;
                    await mockJob.UpdateExportJob(record, x.ArgAt<WeakETag>(1), x.ArgAt<CancellationToken>(2));
                });

                return mockJob;
            }

            var searchService = Substitute.For<ISearchService>();
            searchService.GetSurrogateIdRanges(
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<long>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>()).Returns(new List<(long StartId, long EndId, int Count)>
                {
                    (long.Parse(startId), long.Parse(endId), 50),
                });

            var scoped = Substitute.For<IScoped<ISearchService>>();
            scoped.Value.Returns(searchService);
            Func<IScoped<ISearchService>> factory = () => scoped;

            var processingJob = new ExportProcessingJob(MakeMockJobWithOomOnFirstRange, factory, new TestQueueClient(), new NullLogger<ExportProcessingJob>());
            var jobRecord = GenerateJobRecord(OperationStatus.Completed, exportJobType: exportJobType, resourceType: resourceType, startSurrogateId: startId, endSurrogateId: endId);
            var jobInfo = GenerateJobInfo(jobRecord);

            var result = await processingJob.ExecuteAsync(jobInfo, CancellationToken.None);

            Assert.True(callCount > 1);
            Assert.NotNull(result);

            // Verify GetSurrogateIdRanges was called with correct resource type and range
            await searchService.Received(1).GetSurrogateIdRanges(
                expectedRangeResourceType,
                long.Parse(startId),
                long.Parse(endId),
                Arg.Any<int>(),
                Arg.Any<int>(),
                true,
                Arg.Any<CancellationToken>(),
                true);
        }

        [Fact]
        public async Task GivenSqlExportJob_WhenOomOccurs_ThenSubRangesAreProcessedWithReducedBatchSize()
        {
            var capturedStartIds = new List<string>();
            var capturedEndIds = new List<string>();
            uint capturedBatchSize = 0;
            int callCount = 0;

            IExportJobTask MakeMockJobTrackingRanges()
            {
                var mockJob = Substitute.For<IExportJobTask>();
                mockJob.ExecuteAsync(Arg.Any<ExportJobRecord>(), Arg.Any<WeakETag>(), Arg.Any<CancellationToken>()).Returns(async x =>
                {
                    callCount++;
                    var record = x.ArgAt<ExportJobRecord>(0);

                    if (callCount == 1)
                    {
                        throw new OutOfMemoryException("Simulated OOM");
                    }

                    // Capture the sub-range parameters passed to each retry
                    capturedStartIds.Add(record.StartSurrogateId);
                    capturedEndIds.Add(record.EndSurrogateId);
                    capturedBatchSize = record.MaximumNumberOfResourcesPerQuery;

                    record.Status = OperationStatus.Completed;
                    await mockJob.UpdateExportJob(record, x.ArgAt<WeakETag>(1), x.ArgAt<CancellationToken>(2));
                });

                return mockJob;
            }

            var searchService = Substitute.For<ISearchService>();
            searchService.GetSurrogateIdRanges(
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<long>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>()).Returns(new List<(long StartId, long EndId, int Count)>
                {
                    (100, 150, 50),
                    (150, 200, 50),
                });

            var scoped = Substitute.For<IScoped<ISearchService>>();
            scoped.Value.Returns(searchService);
            Func<IScoped<ISearchService>> factory = () => scoped;

            var processingJob = new ExportProcessingJob(MakeMockJobTrackingRanges, factory, new TestQueueClient(), new NullLogger<ExportProcessingJob>());
            var jobRecord = GenerateJobRecord(OperationStatus.Completed, resourceType: "Patient", startSurrogateId: "100", endSurrogateId: "200");
            var jobInfo = GenerateJobInfo(jobRecord);

            await processingJob.ExecuteAsync(jobInfo, CancellationToken.None);

            // Verify GetSurrogateIdRanges was called
            await searchService.Received(1).GetSurrogateIdRanges(
                "Patient",
                100L,
                200L,
                Arg.Any<int>(),
                Arg.Any<int>(),
                true,
                Arg.Any<CancellationToken>(),
                true);

            // Verify both sub-ranges were processed
            Assert.Equal(2, capturedStartIds.Count);
            Assert.Equal("100", capturedStartIds[0]);
            Assert.Equal("150", capturedStartIds[1]);
            Assert.Equal("150", capturedEndIds[0]);
            Assert.Equal("200", capturedEndIds[1]);

            // Verify batch size was reduced (100 / 10 = 10)
            Assert.Equal(10u, capturedBatchSize);
        }

        [Theory]
        [InlineData(null, null)] // Cosmos path
        [InlineData("100", "200")] // SQL path
        public async Task GivenExportJob_WhenOomExceedsMaxReductions_ThenFailureMessageContainsCurrentBatchSize(string startSurrogateId, string endSurrogateId)
        {
            IExportJobTask MakeMockJobThatAlwaysOoms()
            {
                var mockJob = Substitute.For<IExportJobTask>();
                mockJob.ExecuteAsync(Arg.Any<ExportJobRecord>(), Arg.Any<WeakETag>(), Arg.Any<CancellationToken>()).Returns<Task>(x =>
                {
                    throw new OutOfMemoryException("Persistent OOM");
                });

                return mockJob;
            }

            var searchService = Substitute.For<ISearchService>();
            searchService.GetSurrogateIdRanges(
                Arg.Any<string>(),
                Arg.Any<long>(),
                Arg.Any<long>(),
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<bool>()).Returns(new List<(long StartId, long EndId, int Count)>
                {
                    (100, 150, 50),
                    (150, 200, 50),
                });

            var scoped = Substitute.For<IScoped<ISearchService>>();
            scoped.Value.Returns(searchService);
            Func<IScoped<ISearchService>> factory = () => scoped;

            var processingJob = new ExportProcessingJob(MakeMockJobThatAlwaysOoms, factory, new TestQueueClient(), new NullLogger<ExportProcessingJob>());
            var jobRecord = GenerateJobRecord(OperationStatus.Completed, resourceType: "Patient", startSurrogateId: startSurrogateId, endSurrogateId: endSurrogateId);
            var jobInfo = GenerateJobInfo(jobRecord);

            var ex = await Assert.ThrowsAsync<JobExecutionException>(() => processingJob.ExecuteAsync(jobInfo, CancellationToken.None));
            var failedRecord = (ExportJobRecord)ex.Error;

            // The failure message should contain the final (reduced) batch size, not the original
            Assert.Contains(failedRecord.MaximumNumberOfResourcesPerQuery.ToString(), failedRecord.FailureDetails.FailureReason);
        }

        [Fact]
        public async Task GivenCosmosExportJobWithLargeBatchSize_WhenOomExceedsMaxReductions_ThenFailsViaReductionCountBeforeBatchMinimum()
        {
            // With batch size 10000 and OomReductionFactor=10, Cosmos path reductions are: 10000→1000→100→10
            // MaxOomReductionsBeforeSoftFail=3 so the 4th OOM should fail via reductionCount (count=4 > 3),
            // NOT via minimum batch size. This verifies the reductionCount check fires before TryReduceEffectiveBatchSize.
            var mockJob = Substitute.For<IExportJobTask>();
            mockJob.ExecuteAsync(Arg.Any<ExportJobRecord>(), Arg.Any<WeakETag>(), Arg.Any<CancellationToken>()).Returns<Task>(x =>
            {
                throw new OutOfMemoryException("Persistent OOM");
            });

            var scoped = Substitute.For<IScoped<ISearchService>>();
            Func<IScoped<ISearchService>> factory = () => scoped;

            var processingJob = new ExportProcessingJob(() => mockJob, factory, new TestQueueClient(), new NullLogger<ExportProcessingJob>());
            var jobRecord = GenerateJobRecord(OperationStatus.Completed, resourceType: "Patient", maximumNumberOfResourcesPerQuery: 10000);
            var jobInfo = GenerateJobInfo(jobRecord);

            var ex = await Assert.ThrowsAsync<JobExecutionException>(() => processingJob.ExecuteAsync(jobInfo, CancellationToken.None));
            var failedRecord = (ExportJobRecord)ex.Error;
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, failedRecord.FailureDetails.FailureStatusCode);

            // Batch size should still be 10 (3 successful reductions: 10000→1000→100→10), not 1.
            // The 4th OOM triggers reductionCount > MaxOomReductionsBeforeSoftFail BEFORE another reduction occurs.
            Assert.Equal(10u, failedRecord.MaximumNumberOfResourcesPerQuery);
        }
    }
}
