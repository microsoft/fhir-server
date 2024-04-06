// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Health.Extensions.Xunit;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Xunit.Abstractions;
using JobStatus = Microsoft.Health.JobManagement.JobStatus;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    /// <summary>
    /// Use different queue type for integration test to avoid conflict
    /// </summary>
    internal enum TestQueueType : byte
    {
        GivenNewJobs_WhenEnqueueJobs_ThenCreatedJobsShouldBeReturned = 16,
        GivenNewJobsWithSameQueueType_WhenEnqueueWithForceOneActiveJobGroup_ThenSecondJobShouldNotBeEnqueued,
        GivenJobsWithSameDefinition_WhenEnqueue_ThenOnlyOneJobShouldBeEnqueued,
        GivenJobsWithSameDefinition_WhenEnqueueWithGroupId_ThenGroupIdShouldBeCorrect,
        GivenJobsEnqueue_WhenDequeue_ThenAllJobsShouldBeReturned,
        GivenJobWithExpiredHeartbeat_WhenDequeue_ThenJobWithResultShouldBeReturned,
        GivenRunningJobCancelled_WhenHeartbeat_ThenCancelRequestedShouldBeReturned,
        GivenJobNotHeartbeat_WhenDequeue_ThenJobShouldBeReturnedAgain,
        GivenGroupJobs_WhenCompleteJob_ThenJobsShouldBeCompleted,
        GivenGroupJobs_WhenCancelJobsByGroupId_ThenAllJobsShouldBeCancelled,
        GivenGroupJobs_WhenCancelJobsById_ThenOnlySingleJobShouldBeCancelled,
        GivenGroupJobs_WhenCancelJobsByGroupIdCalledTwice_ThenJobStatusShouldNotChange,
        GivenGroupJobs_WhenOneJobFailedAndRequestCancellation_ThenAllJobsShouldBeCancelled,
        ExecuteWithHeartbeat,
        ExecuteWithHeartbeatsHeavy,
    }

    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    [FhirStorageTestsFixtureArgumentSets(DataStore.All)]
    public class QueueClientTests : IClassFixture<FhirStorageTestsFixture>
    {
        private readonly ITestOutputHelper _testOutputHelper;
        private readonly FhirStorageTestsFixture _fixture;
        private readonly IQueueClient _queueClient;

        public QueueClientTests(FhirStorageTestsFixture fixture, ITestOutputHelper testOutputHelper)
        {
            _fixture = fixture;
            _testOutputHelper = testOutputHelper;
            _queueClient = fixture.QueueClient;
        }

        [Fact]
        public async Task GivenNewJobs_WhenEnqueueJobs_ThenCreatedJobsShouldBeReturned()
        {
            byte queueType = (byte)TestQueueType.GivenNewJobs_WhenEnqueueJobs_ThenCreatedJobsShouldBeReturned;

            string[] definitions = new[] { "job1", "job2" };
            IEnumerable<JobInfo> jobInfos = await _queueClient.EnqueueAsync(queueType, definitions, null, false, false, CancellationToken.None);

            Assert.Equal(2, jobInfos.Count());
            Assert.Equal(1, jobInfos.Last().Id - jobInfos.First().Id);
            Assert.Equal(JobStatus.Created, jobInfos.First().Status);
            Assert.Null(jobInfos.First().StartDate);
            Assert.Null(jobInfos.First().EndDate);
            Assert.Equal(jobInfos.Last().GroupId, jobInfos.First().GroupId);

            JobInfo jobInfo = await _queueClient.GetJobByIdAsync(queueType, jobInfos.First().Id, true, CancellationToken.None);
            Assert.Contains(jobInfo.Definition, definitions);

            jobInfo = await _queueClient.GetJobByIdAsync(queueType, jobInfos.Last().Id, true, CancellationToken.None);
            Assert.Contains(jobInfo.Definition, definitions);
        }

        [Fact]
        public async Task GivenNewJobsWithSameQueueType_WhenEnqueueWithForceOneActiveJobGroup_ThenSecondJobShouldNotBeEnqueued()
        {
            byte queueType = (byte)TestQueueType.GivenNewJobsWithSameQueueType_WhenEnqueueWithForceOneActiveJobGroup_ThenSecondJobShouldNotBeEnqueued;

            IEnumerable<JobInfo> jobInfos = await _queueClient.EnqueueAsync(queueType, new[] { "job1" }, null, true, false, CancellationToken.None);
            await Assert.ThrowsAsync<JobConflictException>(async () => await _queueClient.EnqueueAsync(queueType, new[] { "job2" }, null, true, false, CancellationToken.None));
        }

        [Fact]
        public async Task GivenJobsWithSameDefinition_WhenEnqueue_ThenOnlyOneJobShouldBeEnqueued()
        {
            byte queueType = (byte)TestQueueType.GivenJobsWithSameDefinition_WhenEnqueue_ThenOnlyOneJobShouldBeEnqueued;

            IEnumerable<JobInfo> jobInfos = await _queueClient.EnqueueAsync(queueType, new[] { "job1"}, null, false, false, CancellationToken.None);
            Assert.Single(jobInfos);
            long jobId = jobInfos.First().Id;
            jobInfos = await _queueClient.EnqueueAsync(queueType, new[] { "job1"}, null, false, false, CancellationToken.None);
            Assert.Equal(jobId, jobInfos.First().Id);
        }

        [Fact]
        public async Task GivenJobsWithSameDefinition_WhenEnqueueWithGroupId_ThenGroupIdShouldBeCorrect()
        {
            byte queueType = (byte)TestQueueType.GivenJobsWithSameDefinition_WhenEnqueueWithGroupId_ThenGroupIdShouldBeCorrect;

            long groupId = new Random().Next(int.MinValue, int.MaxValue);
            IEnumerable<JobInfo> jobInfos = await _queueClient.EnqueueAsync(queueType, new[] { "job1", "job2" }, groupId, false, false, CancellationToken.None);
            Assert.Equal(2, jobInfos.Count());
            Assert.Equal(groupId, jobInfos.First().GroupId);
            Assert.Equal(groupId, jobInfos.Last().GroupId);
            jobInfos = await _queueClient.EnqueueAsync(queueType, new[] { "job3", "job4"}, groupId, false, false, CancellationToken.None);
            Assert.Equal(2, jobInfos.Count());
            Assert.Equal(groupId, jobInfos.First().GroupId);
            Assert.Equal(groupId, jobInfos.Last().GroupId);
        }

        [Fact]
        public async Task GivenJobsEnqueue_WhenDequeue_ThenAllJobsShouldBeReturned()
        {
            byte queueType = (byte)TestQueueType.GivenJobsEnqueue_WhenDequeue_ThenAllJobsShouldBeReturned;

            await _queueClient.EnqueueAsync(queueType, new[] { "job1" }, null, false, false, CancellationToken.None);
            await _queueClient.EnqueueAsync(queueType, new[] { "job2" }, null, false, false, CancellationToken.None);

            List<string> definitions = new List<string>();
            JobInfo jobInfo1 = await _queueClient.DequeueAsync(queueType, "test-worker", 10, CancellationToken.None);
            definitions.Add(jobInfo1.Definition);
            JobInfo jobInfo2 = await _queueClient.DequeueAsync(queueType, "test-worker", 10, CancellationToken.None);
            definitions.Add(jobInfo2.Definition);
            Assert.Null(await _queueClient.DequeueAsync(queueType, "test-worker", 10, CancellationToken.None));

            Assert.Contains("job1", definitions);
            Assert.Contains("job2", definitions);
        }

        [Fact]
        [Obsolete("Unit test for obsolete method")]
        public async Task GivenJobWithExpiredHeartbeat_WhenDequeue_ThenJobWithResultShouldBeReturned()
        {
            byte queueType = (byte)TestQueueType.GivenJobWithExpiredHeartbeat_WhenDequeue_ThenJobWithResultShouldBeReturned;

            await _queueClient.EnqueueAsync(queueType, new[] { "job1" }, null, false, false, CancellationToken.None);

            JobInfo jobInfo1 = await _queueClient.DequeueAsync(queueType, "test-worker", 1, CancellationToken.None);
            jobInfo1.QueueType = queueType;
            jobInfo1.Result = "current-result";
            await JobHosting.ExecuteJobWithHeavyHeartbeatsAsync(
                _queueClient,
                jobInfo1,
                async cancelSource =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(2));
                    return jobInfo1.Result;
                },
                TimeSpan.FromSeconds(0.1),
                new CancellationTokenSource());
            await Task.Delay(TimeSpan.FromSeconds(1));
            JobInfo jobInfo2 = await _queueClient.DequeueAsync(queueType, "test-worker", 0, CancellationToken.None);

            Assert.Equal(jobInfo1.Result, jobInfo2?.Result);
        }

        [Fact]
        [Obsolete("Unit test for obsolete method")]
        public async Task GivenRunningJobCancelled_WhenHeartbeat_ThenCancelRequestedShouldBeReturned()
        {
            byte queueType = (byte)TestQueueType.GivenRunningJobCancelled_WhenHeartbeat_ThenCancelRequestedShouldBeReturned;

            await _queueClient.EnqueueAsync(queueType, new[] { "job" }, null, false, false, CancellationToken.None);

            var job = await _queueClient.DequeueAsync(queueType, "test-worker", 10, CancellationToken.None);
            job.QueueType = queueType;
            await _queueClient.CancelJobByGroupIdAsync(queueType, job.GroupId, CancellationToken.None);
            try
            {
                await JobHosting.ExecuteJobWithHeavyHeartbeatsAsync(
                    _queueClient,
                    job,
                    async cancelSource =>
                    {
                        await Task.Delay(TimeSpan.FromSeconds(10), cancelSource.Token);
                        return job.Result;
                    },
                    TimeSpan.FromSeconds(0.1),
                    new CancellationTokenSource());
            }
            catch (TaskCanceledException)
            {
                // do nothing
            }

            Assert.Equal(JobStatus.Running, job.Status);
            job = await _queueClient.GetJobByIdAsync(queueType, job.Id, false, CancellationToken.None);
            Assert.True(job.CancelRequested);
        }

        [Fact]
        public async Task GivenJobNotHeartbeat_WhenDequeue_ThenJobShouldBeReturnedAgain()
        {
            byte queueType = (byte)TestQueueType.GivenJobNotHeartbeat_WhenDequeue_ThenJobShouldBeReturnedAgain;

            await _queueClient.EnqueueAsync(queueType, new[] { "job1" }, null, false, false, CancellationToken.None);

            JobInfo jobInfo1 = await _queueClient.DequeueAsync(queueType, "test-worker", 0, CancellationToken.None);
            await Task.Delay(TimeSpan.FromSeconds(1));
            JobInfo jobInfo2 = await _queueClient.DequeueAsync(queueType, "test-worker", 0, CancellationToken.None);
            Assert.Null(await _queueClient.DequeueAsync(queueType, "test-worker", 10, CancellationToken.None));

            Assert.Equal(jobInfo1.Id, jobInfo2.Id);
            Assert.True(jobInfo1.Version < jobInfo2.Version);
        }

        [Fact]
        public async Task GivenGroupJobs_WhenCompleteJob_ThenJobsShouldBeCompleted()
        {
            byte queueType = (byte)TestQueueType.GivenGroupJobs_WhenCompleteJob_ThenJobsShouldBeCompleted;

            await _queueClient.EnqueueAsync(queueType, new[] { "job1", "job2" }, null, false, false, CancellationToken.None);

            JobInfo jobInfo1 = await _queueClient.DequeueAsync(queueType, "test-worker", 0, CancellationToken.None);
            JobInfo jobInfo2 = await _queueClient.DequeueAsync(queueType, "test-worker", 10, CancellationToken.None);

            Assert.Equal(JobStatus.Running, jobInfo1.Status);
            jobInfo1.Status = JobStatus.Failed;
            jobInfo1.Result = "Failed for cancellation";
            await _queueClient.CompleteJobAsync(jobInfo1, false, CancellationToken.None);
            JobInfo jobInfo = await _queueClient.GetJobByIdAsync(queueType, jobInfo1.Id, false, CancellationToken.None);
            Assert.Equal(JobStatus.Failed, jobInfo.Status);
            Assert.Equal(jobInfo1.Result, jobInfo.Result);

            jobInfo2.Status = JobStatus.Completed;
            jobInfo2.Result = "Completed";
            await _queueClient.CompleteJobAsync(jobInfo2, false, CancellationToken.None);
            jobInfo = await _queueClient.GetJobByIdAsync(queueType, jobInfo2.Id, false, CancellationToken.None);
            Assert.Equal(JobStatus.Completed, jobInfo.Status);
            Assert.Equal(jobInfo2.Result, jobInfo.Result);
        }

        [Fact]
        public async Task GivenGroupJobs_WhenCancelJobsByGroupId_ThenAllJobsShouldBeCancelled()
        {
            byte queueType = (byte)TestQueueType.GivenGroupJobs_WhenCancelJobsByGroupId_ThenAllJobsShouldBeCancelled;

            await _queueClient.EnqueueAsync(queueType, new[] { "job1", "job2", "job3" }, null, false, false, CancellationToken.None);

            JobInfo jobInfo1 = await _queueClient.DequeueAsync(queueType, "test-worker", 0, CancellationToken.None);
            JobInfo jobInfo2 = await _queueClient.DequeueAsync(queueType, "test-worker", 0, CancellationToken.None);

            await _queueClient.CancelJobByGroupIdAsync(queueType, jobInfo1.GroupId, CancellationToken.None);
            Assert.True((await _queueClient.GetJobByGroupIdAsync(queueType, jobInfo1.GroupId, false, CancellationToken.None)).All(t => t.Status == JobStatus.Cancelled || (t.Status == JobStatus.Running && t.CancelRequested)));

            jobInfo1.Status = JobStatus.Failed;
            jobInfo1.Result = "Failed for cancellation";
            await _queueClient.CompleteJobAsync(jobInfo1, false, CancellationToken.None);
            JobInfo jobInfo = await _queueClient.GetJobByIdAsync(queueType, jobInfo1.Id, false, CancellationToken.None);
            Assert.Equal(JobStatus.Failed, jobInfo.Status);
            Assert.Equal(jobInfo1.Result, jobInfo.Result);

            jobInfo2.Status = JobStatus.Completed;
            jobInfo2.Result = "Completed";
            await _queueClient.CompleteJobAsync(jobInfo2, false, CancellationToken.None);
            jobInfo = await _queueClient.GetJobByIdAsync(queueType, jobInfo2.Id, false, CancellationToken.None);
            Assert.Equal(JobStatus.Cancelled, jobInfo.Status);
            Assert.Equal(jobInfo2.Result, jobInfo.Result);
        }

        [Fact]
        public async Task GivenGroupJobs_WhenCancelJobsByGroupIdCalledTwice_ThenJobStatusShouldNotChange()
        {
            byte queueType = (byte)TestQueueType.GivenGroupJobs_WhenCancelJobsByGroupIdCalledTwice_ThenJobStatusShouldNotChange;
            await _queueClient.EnqueueAsync(queueType, new string[] { "job1", "job2", "job3" }, null, false, false, CancellationToken.None);

            JobInfo jobInfo1 = await _queueClient.DequeueAsync(queueType, "test-worker", 0, CancellationToken.None);
            JobInfo jobInfo2 = await _queueClient.DequeueAsync(queueType, "test-worker", 0, CancellationToken.None);
            IEnumerable<JobInfo> jobs = await _queueClient.GetJobByGroupIdAsync(queueType, jobInfo1.GroupId, false, CancellationToken.None);
            JobInfo jobInfo3 = jobs.First(t => t.Id != jobInfo1.Id && t.Id != jobInfo2.Id);

            await _queueClient.CancelJobByIdAsync(queueType, jobInfo1.Id, CancellationToken.None);

            jobInfo1.Status = JobStatus.Failed;
            jobInfo1.Result = "job failed";
            await _queueClient.CompleteJobAsync(jobInfo1, false, CancellationToken.None);

            await _queueClient.CancelJobByGroupIdAsync(queueType, jobInfo2.GroupId, CancellationToken.None);
            Assert.True((await _queueClient.GetJobByGroupIdAsync(queueType, jobInfo2.GroupId, false, CancellationToken.None)).All(t => t.Status == JobStatus.Cancelled || t.Status == JobStatus.Failed || (t.Status == JobStatus.Running && t.CancelRequested)));
            jobInfo1 = await _queueClient.GetJobByIdAsync(queueType, jobInfo1.Id, false, CancellationToken.None);
            jobInfo2 = await _queueClient.GetJobByIdAsync(queueType, jobInfo2.Id, false, CancellationToken.None);
            jobInfo3 = await _queueClient.GetJobByIdAsync(queueType, jobInfo3.Id, false, CancellationToken.None);
            Assert.Equal(JobStatus.Failed, jobInfo1.Status);
            Assert.Equal(JobStatus.Running, jobInfo2.Status);
            Assert.Equal(JobStatus.Cancelled, jobInfo3.Status);

            await _queueClient.CancelJobByGroupIdAsync(queueType, jobInfo2.GroupId, CancellationToken.None);
            Assert.True((await _queueClient.GetJobByGroupIdAsync(queueType, jobInfo2.GroupId, false, CancellationToken.None)).All(t => t.Status == JobStatus.Cancelled || t.Status == JobStatus.Failed || (t.Status == JobStatus.Running && t.CancelRequested)));
            jobInfo1 = await _queueClient.GetJobByIdAsync(queueType, jobInfo1.Id, false, CancellationToken.None);
            jobInfo2 = await _queueClient.GetJobByIdAsync(queueType, jobInfo2.Id, false, CancellationToken.None);
            jobInfo3 = await _queueClient.GetJobByIdAsync(queueType, jobInfo3.Id, false, CancellationToken.None);
            Assert.Equal(JobStatus.Failed, jobInfo1.Status);
            Assert.Equal(JobStatus.Running, jobInfo2.Status);
            Assert.Equal(JobStatus.Cancelled, jobInfo3.Status);
        }

        [Fact]
        public async Task GivenGroupJobs_WhenCancelJobsById_ThenOnlySingleJobShouldBeCancelled()
        {
            byte queueType = (byte)TestQueueType.GivenGroupJobs_WhenCancelJobsById_ThenOnlySingleJobShouldBeCancelled;

            IEnumerable<JobInfo> jobs = await _queueClient.EnqueueAsync(queueType, new[] { "job1", "job2", "job3" }, null, false, false, CancellationToken.None);

            await _queueClient.CancelJobByIdAsync(queueType, jobs.First().Id, CancellationToken.None);
            Assert.Equal(JobStatus.Cancelled, (await _queueClient.GetJobByIdAsync(queueType, jobs.First().Id, false, CancellationToken.None)).Status);

            JobInfo jobInfo1 = await _queueClient.DequeueAsync(queueType, "test-worker", 0, CancellationToken.None);
            JobInfo jobInfo2 = await _queueClient.DequeueAsync(queueType, "test-worker", 0, CancellationToken.None);

            Assert.False(jobInfo1.CancelRequested);
            Assert.False(jobInfo2.CancelRequested);
        }

        [Fact]
        public async Task GivenGroupJobs_WhenOneJobFailedAndRequestCancellation_ThenAllJobsShouldBeCancelled()
        {
            byte queueType = (byte)TestQueueType.GivenGroupJobs_WhenOneJobFailedAndRequestCancellation_ThenAllJobsShouldBeCancelled;

            await _queueClient.EnqueueAsync(queueType, new[] { "job1", "job2", "job3" }, null, false, false, CancellationToken.None);

            JobInfo jobInfo1 = await _queueClient.DequeueAsync(queueType, "test-worker", 0, CancellationToken.None);
            jobInfo1.Status = JobStatus.Failed;
            jobInfo1.Result = "Failed for critical error";

            await _queueClient.CompleteJobAsync(jobInfo1, true, CancellationToken.None);
            Assert.True((await _queueClient.GetJobByGroupIdAsync(queueType, jobInfo1.GroupId, false, CancellationToken.None)).All(t => t.Status is (JobStatus?)JobStatus.Cancelled or (JobStatus?)JobStatus.Failed));
        }

        [Fact(Skip ="Doesn't run within time limits. Bug: 103102")]
        public async Task GivenAJob_WhenExecutedWithHeartbeats_ThenHeartbeatsAreRecorded()
        {
            await this.RetryAsync(
                async () =>
                {
                    var queueType = (byte)TestQueueType.ExecuteWithHeartbeat;
                    await _queueClient.EnqueueAsync(queueType, new[] { "job" }, null, false, false, CancellationToken.None);
                    JobInfo job = await _queueClient.DequeueAsync(queueType, "test-worker", 1, CancellationToken.None);
                    var cancel = new CancellationTokenSource();
                    cancel.CancelAfter(TimeSpan.FromSeconds(30));
                    Task<string> execTask = JobHosting.ExecuteJobWithHeartbeatsAsync(
                        _queueClient,
                        queueType,
                        job.Id,
                        job.Version,
                        async cancelSource =>
                        {
                            await Task.Delay(TimeSpan.FromSeconds(10), cancelSource.Token);
                            await _queueClient.CompleteJobAsync(job, false, cancelSource.Token);
                            return "Test";
                        },
                        TimeSpan.FromSeconds(1),
                        cancel);

                    var currentJob = job;
                    var previousJob = job;
                    var heartbeatChanges = 0;
                    var dequeueTask = Task.Run(
                        async () =>
                        {
                            while (currentJob.Status == JobStatus.Running)
                            {
                                await Task.Delay(TimeSpan.FromSeconds(1), cancel.Token);
                                currentJob = await _queueClient.GetJobByIdAsync(queueType, job.Id, true, cancel.Token);
                                if (currentJob.HeartbeatDateTime != previousJob.HeartbeatDateTime)
                                {
                                    heartbeatChanges++;
                                    previousJob = currentJob;
                                }
                            }
                        },
                        cancel.Token);
                    Task.WaitAll(execTask, dequeueTask);

                    Assert.True(heartbeatChanges >= 1, $"Heartbeats recorded: ${heartbeatChanges}");
                });
        }

        [Fact(Skip = "Doesn't run within time limits. Bug: 103102")]
        [Obsolete("Unit test for obsolete method")]
        public async Task GivenAJob_WhenExecutedWithHeavyHeartbeats_ThenHeavyHeartbeatsAreRecorded()
        {
            await this.RetryAsync(
                async () =>
                {
                    var queueType = (byte)TestQueueType.ExecuteWithHeartbeatsHeavy;
                    await _queueClient.EnqueueAsync(queueType, new[] { "job" }, null, false, false, CancellationToken.None);
                    JobInfo job = await _queueClient.DequeueAsync(queueType, "test-worker", 1, CancellationToken.None);
                    var cancel = new CancellationTokenSource();
                    cancel.CancelAfter(TimeSpan.FromSeconds(30));
                    var execTask = JobHosting.ExecuteJobWithHeavyHeartbeatsAsync(
                        _queueClient,
                        job,
                        async cancelSource =>
                        {
                            await Task.Delay(TimeSpan.FromSeconds(5), cancelSource.Token);
                            job.Result = "Something";
                            await Task.Delay(TimeSpan.FromSeconds(5));
                            await _queueClient.CompleteJobAsync(job, false, cancelSource.Token);
                            return "Test";
                        },
                        TimeSpan.FromSeconds(1),
                        cancel);

                    var currentJob = job;
                    var previousJob = job;
                    var heartbeatChanges = 0;
                    var heavyHeartbeatRecorded = false;
                    var dequeueTask = Task.Run(
                        async () =>
                        {
                            while (currentJob.Status == JobStatus.Running)
                            {
                                await Task.Delay(TimeSpan.FromSeconds(1));
                                currentJob = await _queueClient.GetJobByIdAsync(queueType, job.Id, true, cancel.Token);

                                if (currentJob.Status == JobStatus.Running && currentJob.Result != null)
                                {
                                    heavyHeartbeatRecorded = true;
                                }

                                if (currentJob.HeartbeatDateTime != previousJob.HeartbeatDateTime)
                                {
                                    heartbeatChanges++;
                                    previousJob = currentJob;
                                }
                            }
                        },
                        cancel.Token);
                    Task.WaitAll(execTask, dequeueTask);

                    Assert.True(heartbeatChanges >= 1, $"Heartbeats recorded: ${heartbeatChanges}");
                    Assert.True(heavyHeartbeatRecorded, $"Heavy heartbeat not recorded");
                });
        }
    }
}
