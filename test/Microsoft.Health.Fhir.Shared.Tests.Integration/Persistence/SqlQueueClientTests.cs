// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.JobManagement;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
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
        GivenGroupJobs_WhenOneJobFailedAndRequestCancellation_ThenAllJobsShouldBeCancelled,
        ExecuteWithHeartbeat,
        ExecuteWithHeartbeatsHeavy,
    }

    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class SqlQueueClientTests : IClassFixture<SqlServerFhirStorageTestsFixture>
    {
        private readonly SqlServerFhirStorageTestsFixture _fixture;
        private readonly SchemaInformation _schemaInformation;
        private ILogger<SqlQueueClient> _logger = Substitute.For<ILogger<SqlQueueClient>>();
        private readonly ITestOutputHelper _testOutputHelper;

        public SqlQueueClientTests(SqlServerFhirStorageTestsFixture fixture, ITestOutputHelper testOutputHelper)
        {
            _fixture = fixture;
            _schemaInformation = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max);
            _schemaInformation.Current = SchemaVersionConstants.Max;
            _testOutputHelper = testOutputHelper;
        }

        [Fact]
        public async Task GivenNewJobs_WhenEnqueueJobs_ThenCreatedJobsShouldBeReturned()
        {
            byte queueType = (byte)TestQueueType.GivenNewJobs_WhenEnqueueJobs_ThenCreatedJobsShouldBeReturned;

            SqlQueueClient sqlQueueClient = new SqlQueueClient(_fixture.SqlConnectionWrapperFactory, _schemaInformation, _logger);
            string[] definitions = new[] { "job1", "job2" };
            IEnumerable<JobInfo> jobInfos = await sqlQueueClient.EnqueueAsync(queueType, definitions, null, false, false, CancellationToken.None);

            Assert.Equal(2, jobInfos.Count());
            Assert.Equal(1, jobInfos.Last().Id - jobInfos.First().Id);
            Assert.Equal(JobStatus.Created, jobInfos.First().Status);
            Assert.Null(jobInfos.First().StartDate);
            Assert.Null(jobInfos.First().EndDate);
            Assert.Equal(jobInfos.Last().GroupId, jobInfos.First().GroupId);

            JobInfo jobInfo = await sqlQueueClient.GetJobByIdAsync(queueType, jobInfos.First().Id, true, CancellationToken.None);
            Assert.Contains(jobInfo.Definition, definitions);

            jobInfo = await sqlQueueClient.GetJobByIdAsync(queueType, jobInfos.Last().Id, true, CancellationToken.None);
            Assert.Contains(jobInfo.Definition, definitions);
        }

        [Fact]
        public async Task GivenNewJobsWithSameQueueType_WhenEnqueueWithForceOneActiveJobGroup_ThenSecondJobShouldNotBeEnqueued()
        {
            byte queueType = (byte)TestQueueType.GivenNewJobsWithSameQueueType_WhenEnqueueWithForceOneActiveJobGroup_ThenSecondJobShouldNotBeEnqueued;

            SqlQueueClient sqlQueueClient = new SqlQueueClient(_fixture.SqlConnectionWrapperFactory, _schemaInformation, _logger);
            IEnumerable<JobInfo> jobInfos = await sqlQueueClient.EnqueueAsync(queueType, new[] { "job1" }, null, true, false, CancellationToken.None);
            await Assert.ThrowsAsync<JobConflictException>(async () => await sqlQueueClient.EnqueueAsync(queueType, new[] { "job2" }, null, true, false, CancellationToken.None));
        }

        [Fact]
        public async Task GivenJobsWithSameDefinition_WhenEnqueue_ThenOnlyOneJobShouldBeEnqueued()
        {
            byte queueType = (byte)TestQueueType.GivenJobsWithSameDefinition_WhenEnqueue_ThenOnlyOneJobShouldBeEnqueued;

            SqlQueueClient sqlQueueClient = new SqlQueueClient(_fixture.SqlConnectionWrapperFactory, _schemaInformation, _logger);
            IEnumerable<JobInfo> jobInfos = await sqlQueueClient.EnqueueAsync(queueType, new[] { "job1"}, null, false, false, CancellationToken.None);
            Assert.Single(jobInfos);
            long jobId = jobInfos.First().Id;
            jobInfos = await sqlQueueClient.EnqueueAsync(queueType, new[] { "job1"}, null, false, false, CancellationToken.None);
            Assert.Equal(jobId, jobInfos.First().Id);
        }

        [Fact]
        public async Task GivenJobsWithSameDefinition_WhenEnqueueWithGroupId_ThenGroupIdShouldBeCorrect()
        {
            byte queueType = (byte)TestQueueType.GivenJobsWithSameDefinition_WhenEnqueueWithGroupId_ThenGroupIdShouldBeCorrect;

            long groupId = new Random().Next(int.MinValue, int.MaxValue);
            SqlQueueClient sqlQueueClient = new SqlQueueClient(_fixture.SqlConnectionWrapperFactory, _schemaInformation, _logger);
            IEnumerable<JobInfo> jobInfos = await sqlQueueClient.EnqueueAsync(queueType, new[] { "job1", "job2" }, groupId, false, false, CancellationToken.None);
            Assert.Equal(2, jobInfos.Count());
            Assert.Equal(groupId, jobInfos.First().GroupId);
            Assert.Equal(groupId, jobInfos.Last().GroupId);
            jobInfos = await sqlQueueClient.EnqueueAsync(queueType, new[] { "job3", "job4"}, groupId, false, false, CancellationToken.None);
            Assert.Equal(2, jobInfos.Count());
            Assert.Equal(groupId, jobInfos.First().GroupId);
            Assert.Equal(groupId, jobInfos.Last().GroupId);
        }

        [Fact]
        public async Task GivenJobsEnqueue_WhenDequeue_ThenAllJobsShouldBeReturned()
        {
            byte queueType = (byte)TestQueueType.GivenJobsEnqueue_WhenDequeue_ThenAllJobsShouldBeReturned;

            SqlQueueClient sqlQueueClient = new SqlQueueClient(_fixture.SqlConnectionWrapperFactory, _schemaInformation, _logger);
            await sqlQueueClient.EnqueueAsync(queueType, new[] { "job1" }, null, false, false, CancellationToken.None);
            await sqlQueueClient.EnqueueAsync(queueType, new[] { "job2" }, null, false, false, CancellationToken.None);

            List<string> definitions = new List<string>();
            JobInfo jobInfo1 = await sqlQueueClient.DequeueAsync(queueType, "test-worker", 10, CancellationToken.None);
            definitions.Add(jobInfo1.Definition);
            JobInfo jobInfo2 = await sqlQueueClient.DequeueAsync(queueType, "test-worker", 10, CancellationToken.None);
            definitions.Add(jobInfo2.Definition);
            Assert.Null(await sqlQueueClient.DequeueAsync(queueType, "test-worker", 10, CancellationToken.None));

            Assert.Contains("job1", definitions);
            Assert.Contains("job2", definitions);
        }

        [Fact]
        public async Task GivenJobWithExpiredHeartbeat_WhenDequeue_ThenJobWithResultShouldBeReturned()
        {
            byte queueType = (byte)TestQueueType.GivenJobWithExpiredHeartbeat_WhenDequeue_ThenJobWithResultShouldBeReturned;

            SqlQueueClient sqlQueueClient = new SqlQueueClient(_fixture.SqlConnectionWrapperFactory, _schemaInformation, _logger);
            await sqlQueueClient.EnqueueAsync(queueType, new[] { "job1" }, null, false, false, CancellationToken.None);

            JobInfo jobInfo1 = await sqlQueueClient.DequeueAsync(queueType, "test-worker", 1, CancellationToken.None);
            jobInfo1.QueueType = queueType;
            jobInfo1.Result = "current-result";
            await JobHosting.ExecuteJobWithHeavyHeartbeats(
                sqlQueueClient,
                jobInfo1,
                async cancelSource =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(2));
                    return jobInfo1.Result;
                },
                TimeSpan.FromSeconds(0.1),
                new CancellationTokenSource());
            await Task.Delay(TimeSpan.FromSeconds(1));
            JobInfo jobInfo2 = await sqlQueueClient.DequeueAsync(queueType, "test-worker", 0, CancellationToken.None);
            Assert.Equal(jobInfo1.Result, jobInfo2.Result);
        }

        [Fact]
        public async Task GivenRunningJobCancelled_WhenHeartbeat_ThenCancelRequestedShouldBeReturned()
        {
            byte queueType = (byte)TestQueueType.GivenRunningJobCancelled_WhenHeartbeat_ThenCancelRequestedShouldBeReturned;

            var client = new SqlQueueClient(_fixture.SqlConnectionWrapperFactory, _schemaInformation, _logger);
            await client.EnqueueAsync(queueType, new[] { "job" }, null, false, false, CancellationToken.None);

            var job = await client.DequeueAsync(queueType, "test-worker", 10, CancellationToken.None);
            job.QueueType = queueType;
            await client.CancelJobByGroupIdAsync(queueType, job.GroupId, CancellationToken.None);
            try
            {
                await JobHosting.ExecuteJobWithHeavyHeartbeats(
                    client,
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
            job = await client.GetJobByIdAsync(queueType, job.Id, false, CancellationToken.None);
            Assert.True(job.CancelRequested);
        }

        [Fact]
        public async Task GivenJobNotHeartbeat_WhenDequeue_ThenJobShouldBeReturnedAgain()
        {
            byte queueType = (byte)TestQueueType.GivenJobNotHeartbeat_WhenDequeue_ThenJobShouldBeReturnedAgain;

            SqlQueueClient sqlQueueClient = new SqlQueueClient(_fixture.SqlConnectionWrapperFactory, _schemaInformation, _logger);
            await sqlQueueClient.EnqueueAsync(queueType, new[] { "job1" }, null, false, false, CancellationToken.None);

            JobInfo jobInfo1 = await sqlQueueClient.DequeueAsync(queueType, "test-worker", 0, CancellationToken.None);
            await Task.Delay(TimeSpan.FromSeconds(1));
            JobInfo jobInfo2 = await sqlQueueClient.DequeueAsync(queueType, "test-worker", 0, CancellationToken.None);
            Assert.Null(await sqlQueueClient.DequeueAsync(queueType, "test-worker", 10, CancellationToken.None));

            Assert.Equal(jobInfo1.Id, jobInfo2.Id);
            Assert.True(jobInfo1.Version < jobInfo2.Version);
        }

        [Fact]
        public async Task GivenGroupJobs_WhenCompleteJob_ThenJobsShouldBeCompleted()
        {
            byte queueType = (byte)TestQueueType.GivenGroupJobs_WhenCompleteJob_ThenJobsShouldBeCompleted;

            SqlQueueClient sqlQueueClient = new SqlQueueClient(_fixture.SqlConnectionWrapperFactory, _schemaInformation, _logger);
            await sqlQueueClient.EnqueueAsync(queueType, new[] { "job1", "job2" }, null, false, false, CancellationToken.None);

            JobInfo jobInfo1 = await sqlQueueClient.DequeueAsync(queueType, "test-worker", 0, CancellationToken.None);
            JobInfo jobInfo2 = await sqlQueueClient.DequeueAsync(queueType, "test-worker", 10, CancellationToken.None);

            Assert.Equal(JobStatus.Running, jobInfo1.Status);
            jobInfo1.Status = JobStatus.Failed;
            jobInfo1.Result = "Failed for cancellation";
            await sqlQueueClient.CompleteJobAsync(jobInfo1, false, CancellationToken.None);
            JobInfo jobInfo = await sqlQueueClient.GetJobByIdAsync(queueType, jobInfo1.Id, false, CancellationToken.None);
            Assert.Equal(JobStatus.Failed, jobInfo.Status);
            Assert.Equal(jobInfo1.Result, jobInfo.Result);

            jobInfo2.Status = JobStatus.Completed;
            jobInfo2.Result = "Completed";
            await sqlQueueClient.CompleteJobAsync(jobInfo2, false, CancellationToken.None);
            jobInfo = await sqlQueueClient.GetJobByIdAsync(queueType, jobInfo2.Id, false, CancellationToken.None);
            Assert.Equal(JobStatus.Completed, jobInfo.Status);
            Assert.Equal(jobInfo2.Result, jobInfo.Result);
        }

        [Fact]
        public async Task GivenGroupJobs_WhenCancelJobsByGroupId_ThenAllJobsShouldBeCancelled()
        {
            byte queueType = (byte)TestQueueType.GivenGroupJobs_WhenCancelJobsByGroupId_ThenAllJobsShouldBeCancelled;

            SqlQueueClient sqlQueueClient = new SqlQueueClient(_fixture.SqlConnectionWrapperFactory, _schemaInformation, _logger);
            await sqlQueueClient.EnqueueAsync(queueType, new[] { "job1", "job2", "job3" }, null, false, false, CancellationToken.None);

            JobInfo jobInfo1 = await sqlQueueClient.DequeueAsync(queueType, "test-worker", 0, CancellationToken.None);
            JobInfo jobInfo2 = await sqlQueueClient.DequeueAsync(queueType, "test-worker", 0, CancellationToken.None);

            await sqlQueueClient.CancelJobByGroupIdAsync(queueType, jobInfo1.GroupId, CancellationToken.None);
            Assert.True((await sqlQueueClient.GetJobByGroupIdAsync(queueType, jobInfo1.GroupId, false, CancellationToken.None)).All(t => t.Status == JobStatus.Cancelled || (t.Status == JobStatus.Running && t.CancelRequested)));

            jobInfo1.Status = JobStatus.Failed;
            jobInfo1.Result = "Failed for cancellation";
            await sqlQueueClient.CompleteJobAsync(jobInfo1, false, CancellationToken.None);
            JobInfo jobInfo = await sqlQueueClient.GetJobByIdAsync(queueType, jobInfo1.Id, false, CancellationToken.None);
            Assert.Equal(JobStatus.Failed, jobInfo.Status);
            Assert.Equal(jobInfo1.Result, jobInfo.Result);

            jobInfo2.Status = JobStatus.Completed;
            jobInfo2.Result = "Completed";
            await sqlQueueClient.CompleteJobAsync(jobInfo2, false, CancellationToken.None);
            jobInfo = await sqlQueueClient.GetJobByIdAsync(queueType, jobInfo2.Id, false, CancellationToken.None);
            Assert.Equal(JobStatus.Cancelled, jobInfo.Status);
            Assert.Equal(jobInfo2.Result, jobInfo.Result);
        }

        [Fact]
        public async Task GivenGroupJobs_WhenCancelJobsById_ThenOnlySingleJobShouldBeCancelled()
        {
            byte queueType = (byte)TestQueueType.GivenGroupJobs_WhenCancelJobsById_ThenOnlySingleJobShouldBeCancelled;

            var sqlQueueClient = new SqlQueueClient(_fixture.SqlConnectionWrapperFactory, _schemaInformation, _logger);
            IEnumerable<JobInfo> jobs = await sqlQueueClient.EnqueueAsync(queueType, new[] { "job1", "job2", "job3" }, null, false, false, CancellationToken.None);

            await sqlQueueClient.CancelJobByIdAsync(queueType, jobs.First().Id, CancellationToken.None);
            Assert.Equal(JobStatus.Cancelled, (await sqlQueueClient.GetJobByIdAsync(queueType, jobs.First().Id, false, CancellationToken.None)).Status);

            JobInfo jobInfo1 = await sqlQueueClient.DequeueAsync(queueType, "test-worker", 0, CancellationToken.None);
            JobInfo jobInfo2 = await sqlQueueClient.DequeueAsync(queueType, "test-worker", 0, CancellationToken.None);

            Assert.False(jobInfo1.CancelRequested);
            Assert.False(jobInfo2.CancelRequested);
        }

        [Fact]
        public async Task GivenGroupJobs_WhenOneJobFailedAndRequestCancellation_ThenAllJobsShouldBeCancelled()
        {
            byte queueType = (byte)TestQueueType.GivenGroupJobs_WhenOneJobFailedAndRequestCancellation_ThenAllJobsShouldBeCancelled;

            var sqlQueueClient = new SqlQueueClient(_fixture.SqlConnectionWrapperFactory, _schemaInformation, _logger);
            await sqlQueueClient.EnqueueAsync(queueType, new[] { "job1", "job2", "job3" }, null, false, false, CancellationToken.None);

            JobInfo jobInfo1 = await sqlQueueClient.DequeueAsync(queueType, "test-worker", 0, CancellationToken.None);
            jobInfo1.Status = JobStatus.Failed;
            jobInfo1.Result = "Failed for critical error";

            await sqlQueueClient.CompleteJobAsync(jobInfo1, true, CancellationToken.None);
            Assert.True((await sqlQueueClient.GetJobByGroupIdAsync(queueType, jobInfo1.GroupId, false, CancellationToken.None)).All(t => t.Status is (JobStatus?)JobStatus.Cancelled or (JobStatus?)JobStatus.Failed));
        }

        [Fact]
        public async Task ExecuteWithHeartbeats()
        {
            var queueType = (byte)TestQueueType.ExecuteWithHeartbeat;
            var client = new SqlQueueClient(_fixture.SqlConnectionWrapperFactory, _schemaInformation, XUnitLogger<SqlQueueClient>.Create(_testOutputHelper));
            await client.EnqueueAsync(queueType, new[] { "job" }, null, false, false, CancellationToken.None);
            JobInfo job = await client.DequeueAsync(queueType, "test-worker", 1, CancellationToken.None);
            var cancel = new CancellationTokenSource();
            cancel.CancelAfter(TimeSpan.FromSeconds(30));
            var execDate = DateTime.UtcNow;
            var dequeueDate = DateTime.UtcNow;
            var execTask = JobHosting.ExecuteJobWithHeartbeats(
                client,
                queueType,
                job.Id,
                job.Version,
                async cancel =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(10));
                    execDate = DateTime.UtcNow;
                    return "Test";
                },
                TimeSpan.FromSeconds(1),
                cancel);
            var jobInt = (JobInfo)null;
            var dequeueTask = Task.Run(
                async () =>
                {
                    while (jobInt == null)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1));
                        jobInt = await client.DequeueAsync(queueType, "test-worker", 2, cancel.Token);
                    }

                    dequeueDate = DateTime.UtcNow;
                },
                cancel.Token);
            Task.WaitAll(execTask, dequeueTask);

            Assert.Equal(job.Id, jobInt.Id);
            Assert.True(dequeueDate >= execDate, $"dequeue:{dequeueDate} >= exec:{execDate}");
        }

        [Fact]
        public async Task ExecuteWithHeartbeatsHeavy()
        {
            var queueType = (byte)TestQueueType.ExecuteWithHeartbeatsHeavy;
            var client = new SqlQueueClient(_fixture.SqlConnectionWrapperFactory, _schemaInformation, XUnitLogger<SqlQueueClient>.Create(_testOutputHelper));
            await client.EnqueueAsync(queueType, new[] { "job" }, null, false, false, CancellationToken.None);
            JobInfo job = await client.DequeueAsync(queueType, "test-worker", 1, CancellationToken.None);
            var cancel = new CancellationTokenSource();
            cancel.CancelAfter(TimeSpan.FromSeconds(30));
            var execDate = DateTime.UtcNow;
            var dequeueDate = DateTime.UtcNow;
            var execTask = JobHosting.ExecuteJobWithHeavyHeartbeats(
                client,
                job,
                async cancelSource =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(10));
                    execDate = DateTime.UtcNow;
                    return "Test";
                },
                TimeSpan.FromSeconds(1),
                cancel);
            var jobInt = (JobInfo)null;
            var dequeueTask = Task.Run(
                async () =>
                {
                    while (jobInt == null)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1));
                        jobInt = await client.DequeueAsync(queueType, "test-worker", 2, cancel.Token);
                    }

                    dequeueDate = DateTime.UtcNow;
                },
                cancel.Token);
            Task.WaitAll(execTask, dequeueTask);

            Assert.Equal(job.Id, jobInt.Id);
            Assert.True(dequeueDate >= execDate, $"dequeue:{dequeueDate} >= exec:{execDate}");
        }
    }
}
